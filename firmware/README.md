# Scylla firmware

## 바로 플래시

`bastardkb_scylla_splinktegrated_rev1_vial.uf2`를 양쪽 RP2040에 각각 플래시합니다. 왼쪽/오른쪽 전용 파일은 없습니다.

1. RESET을 빠르게 두 번 눌러 `RPI-RP2` 드라이브를 엽니다.
2. UF2를 복사합니다.
3. 양쪽 보드에 반복합니다.
4. AUX/TRRS를 연결하고 오른쪽 half에 USB를 연결합니다.

## 소스 빌드

Vial-QMK 기준 커밋:

`dd43959ae5c08d8a28d38a1acf7b04e86b14a344`

```powershell
git clone --branch vial https://github.com/vial-kb/vial-qmk.git
cd vial-qmk
git checkout dd43959ae5c08d8a28d38a1acf7b04e86b14a344
git submodule update --init --recursive
python -m pip install -r requirements.txt
qmk setup -H (Get-Location).Path
qmk doctor
$Toolkit = 'C:\path\to\this\repository'
Copy-Item -Recurse -Force "$Toolkit\firmware\keyboards" .
qmk compile -kb bastardkb/scylla/splinktegrated_rev1 -km vial
```

생성 파일명은 `bastardkb_scylla_splinktegrated_rev1_vial.uf2`입니다.

## 보존한 하드웨어 설정

- COL: `GP27, GP28, GP21, GP6, GP7, GP8`
- ROW: `GP29, GP26, GP5, GP4, GP9`
- diode: `ROW2COL`
- split serial: `GP1`
- `MASTER_RIGHT`, `SPLIT_USB_DETECT`
- RP2040 / `rp2040` bootloader

`keyboards/bastardkb/scylla` 아래의 경로는 QMK가 요구하는 경로라서 빌드용으로 필요합니다. 그 밖의 디렉터리는 만들지 않았습니다.

앱 빌드와 펌웨어 빌드는 독립적입니다. 앱 UI를 고칠 때는 Vial-QMK를 설치할 필요가 없고, 펌웨어 핀·split 설정을 고칠 때만 이 절차를 사용합니다.

## Vial 잠금 해제

앱에서 `Vial 잠금 해제`를 누르고 왼쪽 Esc와 오른쪽 Backspace를 동시에 누른 상태로 유지합니다.
