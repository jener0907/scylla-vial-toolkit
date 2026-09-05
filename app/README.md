# ScyllaConfigurator

Windows 10/11 x64용 portable Scylla Vial 설정 앱입니다. 설치 프로그램이나 별도 .NET 런타임 없이 실행할 수 있는 self-contained 단일 EXE로 배포합니다.

## 빠른 실행

배포본은 GitHub Releases의 `ScyllaConfigurator.exe`입니다. 개발 작업 폴더의 루트 EXE는 로컬 편의를 위한 복사본일 뿐이며, 새 clone에는 포함되지 않습니다. 오른쪽 half에 USB를 연결하고 AUX/TRRS 케이블로 왼쪽 half를 연결한 뒤 실행합니다.

## 개발 환경과 빌드

필요한 환경은 .NET 8 SDK입니다. 앱 실행은 Windows 전용이며, 공식적으로 검증하는 빌드 환경도 Windows x64입니다. 다른 운영체제에서 Windows targeting을 켜고 빌드할 수는 있지만, 이 저장소의 CI와 배포 파일은 Windows에서 생성합니다.

저장소 루트에서 실행합니다.

```powershell
dotnet --info
dotnet restore app\ScyllaConfigurator.csproj
dotnet build app\ScyllaConfigurator.csproj -c Release
dotnet publish app\ScyllaConfigurator.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\win-x64
```

최종 파일은 `release\win-x64\ScyllaConfigurator.exe`입니다. `release/`, `bin/`, `obj/`는 로컬 빌드 산출물이므로 Git에 커밋하지 않습니다. 현재 저장소 루트의 EXE는 직접 실행하기 위한 로컬 복사본이며 `.gitignore`로 제외되어 있습니다.

## 파일별 책임

| 파일 | 역할 |
|---|---|
| `MainWindow.xaml` | 레이어 탭, 58키 화면, 키 지정, 커스텀 조합키, 매크로 UI |
| `MainWindow.xaml.cs` | 연결 수명주기, 키맵 읽기/쓰기, 입력 모드, 매크로 편집, 저장·검증 |
| `HidDevice.cs` | Windows Raw HID 열거·오픈·읽기·쓰기 |
| `VialClient.cs` | Vial Raw HID 프로토콜 명령과 EEPROM 접근 |
| `MacroCodec.cs` | QMK/Vial 매크로 버퍼 인코딩·디코딩 |
| `Models.cs` | 레이아웃·매크로·커스텀 조합키 모델 |
| `ScyllaLayout.json` | 58키의 화면 위치와 matrix row/col 좌표 |
| `App.xaml` | 색상, 버튼, 입력 컨트롤 등 공통 WPF 스타일 |

## 동작 흐름

```text
HidDevice.FindRaw()
    → VialClient 생성
    → Vial 식별/UID 확인
    → GetKeycode(layer,row,col)
    → 화면 표시
    → SetKeycode / SetMacroBuffer
    → 다시 읽어 저장 결과 검증
```

앱은 일반 키보드 입력 장치가 아니라 Vial Raw HID 인터페이스를 찾습니다. 따라서 OS에서 키 입력이 정상이어도 Vial 펌웨어가 아니면 앱에는 연결되지 않습니다. `vendor_novbus` 펌웨어는 Vial 명령을 제공하지 않으므로 지원 대상이 아닙니다. 연결 시 Vial 응답의 Scylla UID `5B 76 3F FF A8 70 33 C8`도 확인하므로 다른 Vial 키보드는 연결된 장치로 처리하지 않습니다.

## 수정 시 반드시 지킬 것

1. 펌웨어의 matrix GPIO, split serial, `MASTER_RIGHT`, USB master 판별은 앱 수정에서 건드리지 않습니다.
2. Vial 프로토콜 prefix `0xFE`와 현재 32-byte report 처리를 임의로 바꾸지 않습니다.
3. 일반 키맵은 쓴 뒤 read-back으로 검증합니다. 매크로는 쓰기 전에 Vial unlock 상태를 확인하고, 쓴 뒤 다시 읽어 검증합니다.
4. `0x7700`은 일반 키가 아니라 QMK macro slot 0(`매크로 1`)입니다. 매크로 슬롯을 키에 지정할 때 사용합니다.
5. 키맵과 매크로는 키보드 EEPROM에 저장됩니다. `%LOCALAPPDATA%\\ScyllaConfigurator\\custom-combos.json`은 앱에서 만든 커스텀 조합키 목록만 저장합니다.
6. UI 키 위치를 바꿀 때는 XAML이 아니라 `ScyllaLayout.json`의 matrix 좌표와 함께 확인합니다.

## 기능을 추가할 때 찾을 위치

- 키 표시명/레이아웃: `ScyllaLayout.json`, `Models.cs`
- 버튼·탭·색상·크기: `MainWindow.xaml`, `App.xaml`
- 클릭 이벤트와 상태: `MainWindow.xaml.cs`
- 새 Vial 명령: `VialClient.cs`
- 매크로 바이트 포맷: `MacroCodec.cs`
- 장치 검색 실패: `HidDevice.cs`의 usage page, usage, report size, Windows HID 구조체 처리

새 기능은 먼저 UI 상태를 추가하고, 그 다음 `VialClient`의 프로토콜 함수를 연결한 뒤, 장치에 쓴 값을 다시 읽어 검증하는 순서로 구현합니다. 장치가 연결되지 않은 상태에서도 UI가 열리고 테스트되도록 `_client is null` 경로를 유지합니다.

## 빌드 후 확인

```powershell
Test-Path .\release\win-x64\ScyllaConfigurator.exe
Get-FileHash .\release\win-x64\ScyllaConfigurator.exe -Algorithm SHA256
```

실기 확인 순서는 다음과 같습니다.

1. 오른쪽 half에 USB, 양쪽 half 사이에 AUX/TRRS를 연결합니다.
2. 앱을 실행해 `Scylla 연결됨`과 Vial protocol/UID가 표시되는지 확인합니다.
3. Layer 0~3을 읽고 키 하나를 바꾼 뒤 `키맵 저장`을 누릅니다.
4. 앱을 재연결해 변경값이 다시 읽히는지 확인합니다.
5. 매크로는 Vial 잠금 해제 후 저장하고, 슬롯을 다시 읽어 순서가 보존되는지 확인합니다.

## 배포

GitHub 저장소에는 `app/` 소스와 `firmware/` 포트를 커밋하고, 빌드한 `ScyllaConfigurator.exe`와 호환되는 `firmware/bastardkb_scylla_splinktegrated_rev1_vial.uf2`는 GitHub Release asset으로 함께 올립니다. self-contained EXE는 크기가 크므로 일반 소스 파일로 커밋하지 않습니다. 앱 빌드는 `.github/workflows/build-app.yml`에서도 수행합니다.
