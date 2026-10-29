EasyNDF division test
=====================

Adds two test divisions to a new WARNO mod, to check four things in the game before EasyNDF's
Divisions tab relies on them. Both are copies of the 3rd Armored Division under new names.
Nothing outside the test mod is changed.

1. Make a new test mod. In WARNO's Mods folder (Steam: right-click WARNO > Manage > Browse local
   files, then open Mods), right-click an empty spot > Open in Terminal, and run:

       .\CreateNewMod.bat EasyNdfDivisionTest

2. Copy the three files of this folder (Add-TestDivisions.ps1, EZ_Test.png, README.txt) into the new
   EasyNdfDivisionTest folder, next to its GenerateMod.bat.

3. Open a terminal in that folder and run:

       powershell -ExecutionPolicy Bypass -File .\Add-TestDivisions.ps1

   It says "Done" when the divisions are added. Each file it changed is kept as it was, beside
   itself, as <name>.before-test.

4. Run GenerateMod.bat in the same folder.

5. Open C:\Users\<you>\Saved Games\EugenSystems\WARNO\mod\EasyNdfDivisionTest\Config.ini and set
   DeckFormatVersion = 1 (the manual asks for at least 1 in a mod that changes gameplay). Save.

6. Start WARNO, turn the mod on in the Mod Center (restart if it asks), and open the Armory (the
   deck builder). Look at NATO, United States.

What to look for, and send me:

  a. Are "EasyNDF Test A (deck number 500)" and "EasyNDF Test B (deck number 30000)" in the list?
     If they show as EZDIVTEST1 and EZDIVTEST2 instead, the names file is the wrong one.
  b. Test A's emblem: a green shield with "EZ"? Test B's should be the 3rd Armored's.
  c. In each: build a small deck, save it, copy its deck code, then import that code. Does it come
     back as the same division with the same units? (Test B's number, 30000, is the one at risk.)
  d. Optional: play a quick skirmish with one of them.

Screenshots of the list and of anything odd help. To undo everything, delete the mod's folder.
