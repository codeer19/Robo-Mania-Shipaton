# Builds the clean public-repo snapshot of Robo Mania (source + project config only).
import os, shutil, re, sys
SRC = 'E:/Robo Mania'
DST = sys.argv[1] if len(sys.argv) > 1 else 'E:/Robo-Mania-Shipaton'
EXCLUDE_DIRS = {'Assets/_Recovery', 'Assets/Screenshots', 'Assets/Tools/CrazyGamesPromo/work'}
EXCLUDE_EXT = {'.apk', '.aab', '.keystore', '.jks', '.p12', '.mp4', '.mov'}
def copytree(rel):
    for root, dirs, files in os.walk(os.path.join(SRC, rel)):
        r = os.path.relpath(root, SRC).replace(os.sep, '/')
        dirs[:] = [d for d in dirs if f'{r}/{d}' not in EXCLUDE_DIRS and f'{r}/{d}'.rstrip('/') + '.meta' not in EXCLUDE_DIRS]
        for f in files:
            rp = f'{r}/{f}'
            if os.path.splitext(f)[1].lower() in EXCLUDE_EXT: continue
            if any(rp == d + '.meta' for d in EXCLUDE_DIRS): continue
            os.makedirs(os.path.join(DST, r), exist_ok=True)
            shutil.copy2(os.path.join(SRC, rp), os.path.join(DST, rp))
if os.path.exists(DST):
    for name in os.listdir(DST):
        if name == '.git': continue
        p = os.path.join(DST, name)
        shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
os.makedirs(DST, exist_ok=True)
for rel in ('Assets', 'Packages', 'ProjectSettings'):
    copytree(rel)
os.makedirs(f'{DST}/Tools/Shipaton', exist_ok=True)
for f in ('ShipatonSetup.cs', 'make_snapshot.py'):
    shutil.copy2(f'{SRC}/Tools/Shipaton/{f}', f'{DST}/Tools/Shipaton/{f}')
for f in ('README.md', '.gitignore'):
    shutil.copy2(f'{SRC}/Tools/Shipaton/repo/{f}', f'{DST}/{f}')
# Photon App ID: placeholder in the public repo (developers supply their own)
p = f'{DST}/Assets/Photon/Fusion/Resources/PhotonAppSettings.asset'
s = open(p, encoding='utf-8').read()
s = re.sub(r'(AppIdFusion:\s*)\S+', r'\1YOUR_PHOTON_FUSION_APP_ID', s)
open(p, 'w', encoding='utf-8', newline='\n').write(s)
# keystore paths never leave the machine
p = f'{DST}/ProjectSettings/ProjectSettings.asset'
s = open(p, encoding='utf-8').read()
s = re.sub(r'(AndroidKeystoreName:).*', r'\1 ', s); s = re.sub(r'(AndroidKeyaliasName:).*', r'\1 ', s)
open(p, 'w', encoding='utf-8', newline='\n').write(s)
total = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(DST) if '.git' not in r for f in fs)
print('snapshot', DST, round(total / 1e6, 1), 'MB')
