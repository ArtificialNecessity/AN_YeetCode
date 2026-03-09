# HJSON Parser Gold File Testing

## Overview

The HJSON parser includes a gold file testing system that automatically runs on every build. Gold files capture the expected output (either parsed JSON or error diagnostics) for each test HJSON file.

## Usage

### Running Tests

Tests run automatically on every `dotnet build`. You can also run tests manually:

```powershell
# Run tests
dotnet run --project utils/HJSONParserForAI/HJSONParserForAI.csproj --no-build -- --test

# Or just build (tests run automatically)
dotnet build utils/HJSONParserForAI/HJSONParserForAI.csproj
```

### Generating Gold Files

When you add new test files or change the parser behavior, regenerate gold files:

```powershell
dotnet run --project utils/HJSONParserForAI/HJSONParserForAI.csproj -- --gold
```

This will create/update `.hjson.gold` files for all `.hjson` files in `Tests/TestData/`.

## Gold File Format

Each test HJSON file has a corresponding `.gold` file containing the expected output:

- **Valid HJSON files** → Gold file contains the parser diagnostic output showing success
- **Broken HJSON files** → Gold file contains the error diagnostics with repair hypotheses

Example structure:
```
Tests/TestData/
├── valid_simple.hjson          # Input test file
├── valid_simple.hjson.gold     # Expected output
├── broken_unclosed.hjson       # Input with intentional error
└── broken_unclosed.hjson.gold  # Expected error diagnostics
```

## Current Test Coverage

✅ **5 test files:**
- `broken_mismatch.hjson` - Mismatched delimiters
- `broken_multiple.hjson` - Multiple structural errors  
- `broken_unclosed.hjson` - Unclosed delimiters
- `devnull.crucible.hjson` - Real crucible spec
- `valid_simple.hjson` - Valid HJSON control test

## How It Works

1. **Build-Time Testing**: The `.csproj` file includes a PostBuildEvent that runs tests after compilation
2. **Gold File Comparison**: Each test compares actual parser output against the expected gold file
3. **Normalized Comparison**: Output is trimmed and line endings normalized before comparison
4. **Build Failure on Mismatch**: If any test fails, the build fails with detailed diff output

## Implementation

- [`TestHJsonFiles.cs`](utils/HJSONParserForAI/Tests/TestHJsonFiles.cs) - Test runner with gold file logic
- [`HjsonDiagnosticCommandLine.cs`](utils/HJSONParserForAI/HjsonDiagnosticCommandLine.cs) - CLI entry point
- [`HJSONParserForAI.csproj`](utils/HJSONParserForAI/HJSONParserForAI.csproj) - PostBuildEvent configuration

## Design Philosophy

This pattern ensures:
- **Regression detection**: Any parser changes that affect output are immediately caught
- **Self-documenting**: Gold files serve as expected output documentation
- **Zero-friction testing**: Tests run automatically, no manual step required
- **Fail-fast**: Build breaks immediately if tests fail, preventing bad commits