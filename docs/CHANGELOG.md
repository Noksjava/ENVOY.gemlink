# Changelog

## 1.0.0.3A

- Added persistent logging to logs/lastlog.txt alongside the UI log console.
- Hardened Gemini Live audio callback handling and validated resampler buffer sizes.
- Kept Gemini audio buffering aligned to the expected resampler frame size.

## 1.0.0.2A

- Set the application icon to use misc/gemlink.ico in the build output and window chrome.
- Reduced loose files by moving dependency DLLs into a lib subfolder with runtime resolution support.

## 1.0.0.1A

- Added a Gemini voice catalog with descriptive entries for richer UI selection.
- Persisted the chosen Gemini voice in settings and applied it to live audio sessions.
- Added voice selection to the Settings dialog and setup wizard.
- Replaced the harsh 1kHz connection beep with a lower 440Hz tone.

## 1.0.0.0A

- Gemini Live audio integration == stable
- Added resiliency improvements for live reconnects and richer error diagnostics.

## 0.0.0.2PA

- + setup-style controls to Settings, including model fetch, save, and test-mode locking.
- Persisted first-launch state with a local setup marker and only complete when saved.
- Removed garbage notes file from the repository.
- Switched Gemini integration to the Google_GenerativeAI 3.6.3 SDK with normalized model defaults.
- + live Gemini model fetching in the Settings and setup wizard flows.
- Gemini Live audio streaming to forward inbound audio and inject Gemini responses into RTP.

## 0.0.0.1PA

- Introduced GUI
- Settings dialog with a Test mode toggle
- Added Google GenAI SDK wiring
- Added a Gemini model readiness check using a text prompt at startup.
- Added a first-launch setup wizard and TEST API button for Gemini connectivity checks
