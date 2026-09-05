# Repository instructions

이 저장소는 Scylla용 Vial 펌웨어와 Windows 설정 앱을 함께 관리합니다.

## 작업 시작

1. 앱 작업은 `app/README.md`를 먼저 읽습니다.
2. 펌웨어 작업은 `firmware/README.md`를 먼저 읽습니다.
3. 앱만 수정하는 작업에서는 `firmware/keyboards/...`의 matrix, split, GPIO 설정을 변경하지 않습니다.

## 앱 빌드

저장소 루트에서 .NET 8 SDK로 실행합니다.

```powershell
dotnet restore app\ScyllaConfigurator.csproj
dotnet build app\ScyllaConfigurator.csproj -c Release
dotnet publish app\ScyllaConfigurator.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\win-x64
```

산출물은 `release/win-x64/ScyllaConfigurator.exe`입니다. `bin/`, `obj/`, `release/`는 빌드 산출물입니다.

## 앱 수정 규칙

- Vial Raw HID 프로토콜과 32-byte report 처리를 유지합니다.
- 장치에 쓰는 키맵·매크로 값은 read-back으로 확인합니다.
- Vial UID를 바꾸면 `app/MainWindow.xaml.cs`와 펌웨어 `keyboards/bastardkb/scylla/keymaps/vial/config.h`를 함께 확인합니다.
- 화면 키의 matrix 좌표는 `app/ScyllaLayout.json`에서 관리합니다.
- 장치가 없는 환경의 UI 실행 경로를 유지합니다.

## 확인과 보고

- 소스 변경 후 `dotnet build` 또는 publish 결과를 확인합니다.
- 실제 키보드 연결 테스트와 장치 없는 빌드 테스트를 구분해 기록합니다.
- 실행하지 못한 검사나 확인하지 못한 하드웨어 동작을 성공했다고 보고하지 않습니다.
- 펌웨어를 수정했다면 `firmware/README.md`의 Vial-QMK 커밋과 split 설정을 다시 대조합니다.
