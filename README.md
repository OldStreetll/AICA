# AICA - AI Coding Assistant for Visual Studio 2022

> An intelligent AI-powered programming assistant plugin for Visual Studio 2022, featuring Agent capabilities, code understanding, and secure offline operation with private LLM.

## Features

- 🤖 **AI Agent Capabilities** - Autonomous planning, tool calling, and multi-step task execution
- 📖 **Code Understanding** - Read, analyze, and understand your codebase
- ✏️ **Code Operations** - Create, edit, and refactor code with precision
- 🔍 **Code Search** - Search for code, files, and symbols
- 💻 **Command Execution** - Execute terminal commands with safety controls
- 🔒 **Security First** - Confirmation dialogs, command filtering, and .aicaignore support
- 🌐 **Offline/Intranet** - Works with privately deployed LLMs, no external network required

## Requirements

- Visual Studio 2022 (17.0+)
- .NET Framework 4.8
- Private LLM endpoint (OpenAI-compatible API)

## Project Structure

```
AIHelper/
├── AICA.sln                    # Solution file
├── src/
│   ├── AICA.Core/             # Core library (.NET Standard 2.0)
│   │   ├── Agent/             # Agent execution engine
│   │   ├── LLM/               # LLM client interfaces
│   │   ├── Security/          # Safety guards
│   │   └── Prompt/            # Prompt engineering
│   │
│   └── AICA.VSIX/             # VS Extension (.NET Framework 4.8)
│       ├── Commands/          # Menu commands
│       ├── Options/           # Settings pages
│       ├── ToolWindows/       # Chat window
│       └── Resources/         # Icons and assets
```

## Getting Started

### Build from Source

1. Open `AICA.sln` in Visual Studio 2022
2. Build the solution in `Release` mode
3. The VSIX package will be in `src/AICA.VSIX/bin/Release/`

### Configuration

1. Go to `Tools > Options > AICA > General`
2. Configure your LLM endpoint (e.g., `http://localhost:8000/v1/`)
3. Set the model name (e.g., `qwen3-coder`, `deepseek-coder`)

### Usage

- **Open Chat**: `Ctrl+Alt+A` or `View > Other Windows > AICA Chat`
- **Right-click Menu**: Select code and right-click for AICA options
  - Explain Code
  - Refactor
  - Generate Tests

## Development Roadmap

- [x] **Phase 1**: Basic framework and UI
- [ ] **Phase 2**: LLM integration
- [ ] **Phase 3**: Agent core and file tools
- [ ] **Phase 4**: Search tools
- [ ] **Phase 5**: Command execution
- [ ] **Phase 6**: Optimization and testing

## License

MIT License - See [LICENSE.txt](src/AICA.VSIX/LICENSE.txt)
