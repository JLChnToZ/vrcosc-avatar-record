# OSCAvatarRecord

OSCAvatarRecord is a Windows desktop tool for recording VRChat avatar OSC parameter values and sending them back when the avatar changes or resets. It listens for incoming OSC data, stores supported avatar parameters in a local SQLite database, and can selectively re-sync those values to compatible VRChat OSC endpoints.

## What It Does

- Listens for VRChat avatar OSC traffic on a configurable local endpoint. The default endpoint is `127.0.0.1:9001`.
- Detects the active avatar from `/avatar/change` messages.
- Records supported avatar parameter values under the active avatar ID.
- Replays saved values back to VRChat when the active avatar changes.
- Lets you enable or disable synchronization per parameter or for an entire avatar.
- Lets you edit stored values manually and force a one-time resend.
- Persists recorded state between sessions in a local database file.

## Supported Data Types

The recorder accepts OSC values that can be represented as 32-bit primitives:

- `bool`
- signed and unsigned integer values up to 32-bit
- `float`

Parameters with unsupported payload types are ignored.

## Parameters Ignored On Purpose

Some VRChat parameters are intentionally excluded because they are transient, system-managed, or not useful to persist. This includes values such as:

- locomotion and tracking state
- visemes and voice level
- gesture state and gesture weights
- seated, AFK, mute, station, and other session flags
- avatar metadata such as version and scale-derived values

## Requirements

- Windows
- .NET 8 SDK for local development and builds
- A VRChat setup that is emitting OSC traffic to the local machine

## Runtime Behavior

The application is a Windows Forms app targeting `net8.0-windows`.

- It listens for OSC packets on the configured local IP and port.
- It advertises and tracks OSCQuery services to find compatible VRChat clients.
- It waits for `/avatar/change` before associating incoming parameter values with an avatar.
- It stores data in `avistates.dat` in the application directory.
- Fatal startup errors are written to `startup-error.log` in the application directory.

## Using The App

1. Start VRChat with OSC enabled.
2. Launch OSCAvatarRecord.
3. Confirm the OSC endpoint in the status bar. The default is `127.0.0.1:9001`.
4. Click `Connect`.
5. Trigger avatar parameter changes in VRChat.
6. Watch the tree view populate with avatar IDs and parameter values.

### Tree View Actions

- Check or uncheck a parameter to control whether it is automatically synchronized.
- Check or uncheck an avatar node to enable or disable sync for all of its parameters.
- Double-click a selected avatar or parameter to force a one-time sync while connected.
- Press `F2` or use the context menu to edit a stored parameter value.
- Press `Delete` or use the context menu to remove selected parameters or entire avatar entries from the database.
- Hold `Ctrl` to multi-select items before deleting or force syncing.

### Status Messages

The status bar reflects the current runtime state, including:

- database initialization
- connection and disconnection progress
- whether remote VRChat OSC services have been discovered
- whether OSC packets are being received
- whether the app is still waiting for `/avatar/change`

If the app reports that it is connected but not receiving OSC, verify that VRChat is sending OSC output to the same IP and port shown in the status bar.

## Localization

The project includes resource files for these UI languages:

- English
- Japanese
- Korean
- Simplified Chinese
- Traditional Chinese

## Building Locally

From the repository root:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release --runtime win-x64 --self-contained false
```

The published output is written under:

```text
bin/Release/net8.0-windows/win-x64/publish
```

## Manual GitHub Actions Publish Workflow

The repository includes a manual workflow at `.github/workflows/manual-build-publish.yml`.

It performs these steps:

1. Restores dependencies.
2. Builds the project in `Release` mode.
3. Publishes a `win-x64` build.
4. Compresses the publish directory into a zip archive.
5. Uploads the archive as a workflow artifact.

## Main Project Files

- `Program.cs`: application entry point and fatal exception handling
- `Frontend/MainForm.cs`: Windows Forms UI and user interactions
- `Backend/OscRuntimeSession.cs`: runtime orchestration, state sync, and remote sender handling
- `Backend/OscMessageHandler.cs`: OSC message parsing and filtering
- `Backend/AvatarStateRepository.cs`: SQLite persistence layer
- `Backend/OscQueryAdvertiser.cs`: OSCQuery advertisement and remote service discovery

## License

This project is licensed under the MIT License. See `LICENSE`.