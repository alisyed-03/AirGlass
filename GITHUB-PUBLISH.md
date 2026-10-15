# Publishing this project

Use the separate `AirGlass-GitHub.zip` as the repository contents: extract it and upload its files to your repository. It contains the GitHub README, interface/native sources, artwork, licenses and build instructions. It excludes the development workspace, private settings, receiver keys and device logs.

Create a GitHub Release for version 2.5 and attach **AirGlass-Windows.zip** and **AirGlass-Windows.sha256.txt**. These are the complete runnable download and checksum. Users should download this release asset; GitHub's automatic **Source code (zip)** download does not contain the runtime binaries.

Keep the included modified native source archive and all license/provenance files alongside each release. Update the README and artwork when the interface changes. The public repository and release ZIP have not been uploaded by this task.

The executable is not code signed. Test the release on additional clean Windows PCs before promising broad compatibility. Current automated checks, dependency audit and hardware-test limits are documented in `TEST-RESULTS.txt`.
