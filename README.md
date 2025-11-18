# 👽⚡ Zapret CLI

A powerful console application for bypassing DPI (Deep Packet Inspection) on Windows using zapret. Designed for ease of use with comprehensive profile management

### 🔧 Core Functionality
- DPI bypass with multiple configuration profiles
- Real-time service status monitoring
- One-click updates for both zapret and the CLI application
- Interactive profile selection and management

## 🖥️ Requirements

- **Operating System:** Windows 10 or newer
- **Runtime:** .NET 8.0 SDK/Runtime
- **Permissions:** Administrator privileges
- **Internet Connection:** ???

## 📦 Installation

### Option 1: Pre-built Release (Recommended)
1. Download the latest release from the [Releases page](https://github.com/Flowseal/zapret-discord-youtube/releases)
2. Run `Installer.exe` **as Administrator**

### Option 2: Build from Source
1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone the repository:
   ```bash
   git clone https://github.com/Flowseal/zapret-discord-youtube.git
   cd zapret-discord-youtube
   ```
3. Build the application:
   ```bash
   dotnet build -c Release
   ```

## ⚙️ Usage

### Basic Commands
```
start                Start the DPI bypass service with selected profile
stop                 Stop the running service
restart              Restart the service with current profile
status               Display current service status
exit                 Stop the service and exit
```

### Profile Management
```
select [name]        Select profile to use (interactive if no name provided)
list                 List all available profiles
info                 Show details of currently selected profile
test                 Run check of profiles for bypassing effectiveness
```

### Advanced Features
```
add <domain>         Add a new domain to blocklists
update               Check for and install latest version of zapret
update-cli           Update the Zapret CLI application itself
toggle-game-filter   Toggle game filter mode (ports 1024-65535 vs port 12)
game-filter-status   Show current game filter status
```

## 🗺 Roadmap

- [ ] **Auto-start on boot** - Configure service to start automatically with Windows
- [ ] **More interactive interface** - Develop a user-friendly CLI interface
- [ ] **Connection metrics** - Display bandwidth usage and connection statistics

## 👥 Contributing

Contributions are welcome! You can help by:
- Reporting bugs and suggesting features via [Issues](https://github.com/Flowseal/zapret-discord-youtube/issues)
- Submitting pull requests with improvements
- Testing new releases and providing feedback
- Improving documentation

## 📄 License

This project is distributed under the MIT License. See the [LICENSE](LICENSE) file for details.

## 📞 Support

If you encounter any issues or have questions:
- Create an issue in the [GitHub repository](https://github.com/Flowseal/zapret-discord-youtube/issues)
- Twitch: [https://twitch.tv/itzkitb](https://twitch.tv/itzkitb)
- Email: [itzkitb@gmail.com](mailto:itzkitb@gmail.com)

---

*SillyApps :P*