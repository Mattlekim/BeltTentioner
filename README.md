NANO FLASHING AND SETUP

•	Download code and extrect to a folder.

•	Make sure you have installed Arduino IDE.

•	Go in to belt tensioner folder and open belt-tentioner.ino

•	When Auduino IDE click on ports and take not of what com ports if any are available.

•	Plug in your nano yo the computer via any usb cable.

•	Check again under ports and select the newly available port.

•	Search for nano and once you have found it click ok. this will tell the ide what biard you have.

•	Now click the upload button and wait for it to flash the nano.


PUBLISHING A NEW VERSION (AUTO-UPDATE)

•	Bump <Version> in BeltTensionTest.WPF/BeltTensionTest.WPF.csproj.

•	Set $env:BBT_RELEASE_KEY to the BBT_RELEASE_KEY secret (the same value is stored as a GitHub secret on the Riddlersoftgames repo).

•	Run .\publish-release.ps1 -Notes "What changed". It builds the installer (build-installer.ps1), uploads it to riddlersoftgames.co.uk and records it in the site's release index. Every version is kept; the newest is listed on belttentioner.html#downloads.

•	Installed copies ask the site on startup and every 2 days (or Help > Check for Updates), download the new installer in the background, check its sha256 and ask to install. On "Yes" the app closes, a hidden PowerShell script runs the installer silently (Inno Setup closes anything still holding a file) and starts the updated app. Portable (zip) copies are only shown the downloaded installer, since there is nothing to update in place.
