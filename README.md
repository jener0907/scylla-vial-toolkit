# Scylla Vial Toolkit

BastardKB Scylla용 Vial 펌웨어 포트와 Windows 설정 앱입니다.

현재 정상 동작하는 `splinktegrated_rev1` 하드웨어 설정을 유지하면서, 키맵·레이어·매크로를 쉽게 수정할 수 있도록 구성했습니다.

## 프로젝트 개요

BastardKB Scylla의 split 펌웨어는 정상 동작하지만, 브라우저 기반 설정 도구만으로는 키맵·레이어·매크로를 관리하고 저장 상태를 확인하기가 불편했습니다.

이 프로젝트는 기존 `splinktegrated_rev1`의 matrix와 split 통신을 그대로 유지하면서, Windows에서 키맵과 매크로를 직접 설정할 수 있는 가벼운 앱과 재현 가능한 펌웨어 소스를 함께 제공하기 위해 시작했습니다. Release에서는 바로 실행 가능한 앱과 UF2를 받고, 저장소에서는 누구나 소스를 수정·빌드할 수 있습니다.

## 구성

- [`app/`](app/) — Windows 10/11용 portable 설정 앱 소스
- [`firmware/`](firmware/) — Vial 포트, UF2, 펌웨어 안내

앱을 수정하거나 다시 빌드할 때는 [`AGENTS.md`](AGENTS.md)와 [`app/README.md`](app/README.md)의 개발 안내를 먼저 읽습니다.

## 빠른 사용

1. [`firmware/bastardkb_scylla_splinktegrated_rev1_vial.uf2`](firmware/bastardkb_scylla_splinktegrated_rev1_vial.uf2)를 양쪽 RP2040에 각각 플래시합니다.
2. AUX/TRRS 케이블을 연결하고 오른쪽 half에 USB를 연결합니다.
3. GitHub Releases에서 `ScyllaConfigurator.exe`를 다운로드해 실행합니다. 소스 clone에는 큰 EXE를 넣지 않습니다.
4. Vial 잠금 해제가 필요하면 앱의 `Vial 잠금 해제` 버튼을 사용합니다.

앱은 .NET 설치 없이 실행되는 self-contained 단일 EXE입니다. GitHub 일반 파일 제한 때문에 EXE는 저장소에 커밋하지 않고 Release 자산으로 배포하는 것을 권장합니다.

## 펌웨어

펌웨어 포트는 다음을 유지합니다.

- COL: `GP27, GP28, GP21, GP6, GP7, GP8`
- ROW: `GP29, GP26, GP5, GP4, GP9`
- diode: `ROW2COL`
- split serial: `GP1`
- `MASTER_RIGHT`, `SPLIT_USB_DETECT`
- RP2040 / `rp2040` bootloader

빌드 방법은 [`firmware/README.md`](firmware/README.md)를 참고하세요.

## 앱 저장 방식

- 키맵과 매크로: 키보드 EEPROM에 저장
- 커스텀 조합키 목록: `%LOCALAPPDATA%\\ScyllaConfigurator\\custom-combos.json`

매크로는 저장 전에 Vial 잠금 상태를 확인하고, 저장 후 장치에서 다시 읽어 실제 반영 여부를 검증합니다.

## 지원 범위

현재 Release에는 Scylla splinktegrated_rev1용 Vial UF2 하나를 제공합니다. 앱은 해당 Vial 펌웨어의 Raw HID 장치만 지원합니다.

## 라이선스와 출처

소스는 [`LICENSE`](LICENSE)에 적힌 GPL-2.0 조건으로 배포합니다. 펌웨어 포트는 [BastardKB QMK](https://github.com/Bastardkb/bastardkb-qmk)와 [Vial-QMK](https://github.com/vial-kb/vial-qmk)의 코드를 기준으로 합니다. Vial 펌웨어를 빌드할 때 사용하는 정확한 커밋은 [`firmware/README.md`](firmware/README.md)에 기록합니다.
