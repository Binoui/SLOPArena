from pathlib import Path
import re, collections
ROOT=Path.cwd()
PAT=r'CharacterClass|\bMenuRoster\b|\bLegacySelector\b|\bPlayerClass\b|\bCharacterSelection\b|\bClassName\b|\.Class\b|\.Fighter\b(?!\s*\()'
rx=re.compile(PAT)
groups=collections.defaultdict(list)
# Assign each file to one primary concern. Secondary roles are described in the report.
identity={'MatchContentCatalog.cs','MatchContentHandleMap.cs','BuiltInContentResolver.cs','MatchContentCatalogProvider.cs','MatchInstance.cs','MultiMatchOrchestrator.cs','PvPMatch.cs'}
network={'MatchStartRequest.cs','LobbyPlayerInfo.cs','LobbyPayloadCodec.cs','ClientSession.cs','LobbyClient.cs','PracticeSessionLog.cs','ReplayCast.cs'}
menus={'MatchConfig.cs','MenuRoster.cs','CharSelectController.cs','FighterPanel.cs','StageSelectController.cs','LobbyRoomUI.cs','LocalChallengeSetup.cs','LocalPlayMode.cs','WorkshopOverlay.cs'}
presentation={'HUDManager.cs','PlayerRenderer.cs','WeaponAttachConfig.cs','ResultsUI.cs','SoloEndCardView.cs','MatchBase.cs','ReplayMatch.cs','SlopArenaPhotoShoot.cs','CharacterCourseMatch.Scramble.cs','MankiMayhemPresentation.Show.cs'}
for root in ['src/Shared','src/Server','client/Unity/Assets/Scripts','client/Unity/Assets/AbilityLab','tests']:
 for p in sorted(Path(root).rglob('*.cs')):
  if 'obj' in p.parts or 'bin' in p.parts: continue
  lines=p.read_text().splitlines()
  hits=[(i,l.strip()) for i,l in enumerate(lines,1) if rx.search(l)]
  if not hits: continue
  if '/Mayhem/' in str(p) or p.name == 'MoveTakeRig.cs': continue # Fighter here is an entity/state, not fighter identity
  if root=='tests' or 'SelfTest' in p.name: g='Tests'
  elif '/Editor/' in str(p): g='Editor tooling'
  elif p.name in identity:g='Content identity and admission'
  elif p.name in network:g='Network and storage'
  elif p.name in menus:g='Menus and selection'
  elif p.name in presentation:g='Match presentation'
  elif p.name in {'CharacterDefinition.cs','CookedCharacterRuntimeAdapter.cs','CookedCharacterPackageLoader.cs','HurtboxOverride.cs'}:g='Simulation and gameplay'
  else:g='AI, modes and special cases'
  groups[g].append((str(p),hits))
out=Path('tmp/fighter-identity-census')
out.mkdir(parents=True, exist_ok=True)
with (out/'inventory.tsv').open('w') as f:
 f.write('group\tfile\tline\tsource\n')
 for g,files in groups.items():
  for p,hits in files:
   for i,l in hits:f.write(f'{g}\t{p}\t{i}\t{l}\n')
with (out/'inventory.md').open('w') as f:
 f.write('# Search ledger\n\nOne row is one matching source line; comments and declarations count. This is lexical evidence, not an AST call-site count.\n\n')
 for g,files in groups.items():
  f.write(f'## {g}\n\n')
  for p,hits in files:
   f.write(f'### {p}\n\n```text\n')
   for i,l in hits:f.write(f'{i}: {l}\n')
   f.write('```\n\n')
for g,files in groups.items():print(g,len(files),sum(len(h) for _,h in files))
print('TOTAL',sum(len(f) for f in groups.values()),sum(len(h) for f in groups.values() for _,h in f))
# File tables for insertion into the human report.
with (out/'file-tables.md').open('w') as f:
 for g,files in groups.items():
  f.write(f'### {g}\n\n| File (first matching line) | Sites | All matching lines |\n|---|---:|---|\n')
  for p,hits in files:
   nums=', '.join(str(i) for i,_ in hits)
   f.write(f'| `{p}:{hits[0][0]}` | {len(hits)} | {nums} |\n')
  f.write('\n')
