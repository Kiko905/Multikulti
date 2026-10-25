# Multikulti

Mobilná hra pre iOS a Android v Unity, orientácia na šírku.

## Pravidlá vetvenia (Git Flow)
- `main` obsahuje len vydané verzie. Priamo do nej necommitujeme.
- `develop` je hlavná vývojová vetva. Sem sa zlučuje všetka hotová práca.
- Každá úloha z Jiry má vlastnú vetvu z `develop` podľa kľúča, napr. `feature/SCRUM-90-parametre`.
- Do `develop` sa zlučuje cez Pull Request, ktorý schváli aspoň jeden ďalší člen tímu.
- Pred vydaním (napr. na konci sprintu) vznikne z `develop` vetva `release/0.1.0`. Opravujú sa v nej už len chyby. Potom sa zlúči do `main` aj späť do `develop` a v `main` dostane tag `v0.1.0`.
- Naliehavá oprava vydanej verzie ide do vetvy `hotfix/0.1.1` z `main`. Po oprave sa zlúči do `main` aj do `develop`.
- Commit začína kľúčom úlohy, napr. `SCRUM-90 Pridaný konfiguračný súbor`.
- Pred Pull Requestom si stiahni najnovší `develop` a over, že sa projekt otvorí bez chýb.
- Na jednej scéne robí naraz len jeden človek.
