Unit Pictures extractor
=======================

Reads each unit's picture and in-game name from your installed games (WARNO and Steel Division 2),
and saves them in a folder. Nothing in the games' folders is changed.

1. Unzip this folder anywhere, for example to your Desktop.
2. Open a terminal in the folder (in File Explorer: right-click the folder, "Open in Terminal").
3. Run:

       dotnet run

   It finds both games in your Steam libraries. For a game installed elsewhere, give its folder:

       dotnet run -- "D:\Games\WARNO"

4. It saves unit-pictures\WARNO and unit-pictures\SD2: the pictures, units.json (each unit's name and
   picture), a flags folder (each nation's flag, named by its code) and report.txt (what was read,
   and anything it could not read). It takes a few minutes.

For the mock: save the mock's .html file in this same folder, next to unit-pictures, and open it.
To send results back, upload report.txt, archive-paths.txt and archive-summary.txt (and the trees folder, if there is one), not the pictures.
