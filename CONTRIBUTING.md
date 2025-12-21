# Contributing to Replane .NET SDK

Thank you for your interest in contributing! This guide will help you get started.

## Getting Started

### Prerequisites

- **.NET SDK**: Version 10.0 or greater

### Clone the Repository

```sh
git clone https://github.com/replane-dev/replane-dotnet.git
cd replane-dotnet
```

### Restore Dependencies

```sh
dotnet restore
```

## Development

The solution contains the following projects:

- `src/Replane` - Main SDK library
- `tests/Replane.Tests` - Unit tests
- `playground` - Development playground for testing

### Build

```sh
dotnet build
```

### Run Tests

```sh
dotnet test
```

### Run Playground

```sh
dotnet run --project playground
```

## Pull Requests

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes
4. Ensure tests pass: `dotnet test`
5. Ensure the build succeeds: `dotnet build`
6. Commit your changes with a descriptive message
7. Push to your fork and submit a pull request

## Reporting Issues

Found a bug or have a feature request? Please [open an issue](https://github.com/replane-dev/replane-dotnet/issues) on GitHub.

## Community

Have questions or want to discuss Replane? Join the conversation in [GitHub Discussions](https://github.com/orgs/replane-dev/discussions).

## License

By contributing to Replane .NET SDK, you agree that your contributions will be licensed under the MIT License.
