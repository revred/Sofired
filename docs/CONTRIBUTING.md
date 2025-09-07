# Contributing to SOFIRED

Thank you for your interest in contributing to SOFIRED! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

1. [Code of Conduct](#code-of-conduct)
2. [Getting Started](#getting-started)
3. [Development Workflow](#development-workflow)
4. [Code Standards](#code-standards)
5. [Testing Requirements](#testing-requirements)
6. [Documentation Guidelines](#documentation-guidelines)
7. [Pull Request Process](#pull-request-process)
8. [Issue Reporting](#issue-reporting)

## Code of Conduct

By participating in this project, you agree to abide by our Code of Conduct:

- **Be Respectful:** Treat all contributors and users with respect
- **Be Collaborative:** Work together constructively with others
- **Be Professional:** Maintain professional communication
- **Be Inclusive:** Welcome contributions from developers of all backgrounds
- **Focus on Learning:** Help others learn and improve

## Getting Started

### Prerequisites

- **.NET 8.0 SDK** or higher
- **Git** for version control
- **Visual Studio Code** or **Visual Studio 2022** (recommended)
- **ThetaData Terminal** (optional, for real market data)

### Fork and Clone

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/your-username/sofired.git
   cd sofired
   ```
3. Add upstream remote:
   ```bash
   git remote add upstream https://github.com/original/sofired.git
   ```

### Setup Development Environment

1. **Build the solution:**
   ```bash
   dotnet build
   ```

2. **Run tests:**
   ```bash
   dotnet test
   ```

3. **Run backtester:**
   ```bash
   dotnet run --project src/Sofired.Backtester
   ```

## Development Workflow

### Branch Strategy

We use GitFlow branching model:

- **`main`**: Production-ready code
- **`develop`**: Integration branch for features
- **`feature/feature-name`**: Individual feature branches
- **`hotfix/issue-description`**: Critical bug fixes
- **`release/version-number`**: Release preparation

### Feature Development

1. **Create feature branch:**
   ```bash
   git checkout develop
   git pull upstream develop
   git checkout -b feature/your-feature-name
   ```

2. **Make changes with commits:**
   ```bash
   git add .
   git commit -m "feat: add new options pricing model"
   ```

3. **Keep branch updated:**
   ```bash
   git fetch upstream
   git rebase upstream/develop
   ```

4. **Push and create PR:**
   ```bash
   git push origin feature/your-feature-name
   ```

### Commit Message Guidelines

Use conventional commits format:

```
type(scope): description

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes
- `refactor`: Code refactoring
- `test`: Adding/updating tests
- `chore`: Maintenance tasks

**Examples:**
```bash
feat(trading): add Kelly Criterion position sizing
fix(risk): correct VaR calculation for options spreads
docs(api): update RealOptionsEngine documentation
test(integration): add multi-symbol backtest scenarios
```

## Code Standards

### C# Coding Conventions

Follow Microsoft C# coding conventions with these additions:

#### Naming Conventions
```csharp
// Classes: PascalCase
public class RealOptionsEngine

// Methods: PascalCase
public async Task<RealOptionsPricing> GetPutSpreadPricing()

// Variables: camelCase
var currentPrice = 15.0m;

// Constants: UPPER_CASE
private const decimal MAX_POSITION_SIZE = 0.25m;

// Private fields: _camelCase
private readonly IThetaDataClient _thetaClient;
```

#### Code Organization
```csharp
// Order of class members:
public class ExampleClass
{
    // 1. Constants
    private const decimal DEFAULT_VIX = 20.0m;
    
    // 2. Fields
    private readonly IService _service;
    
    // 3. Constructor
    public ExampleClass(IService service) => _service = service;
    
    // 4. Properties
    public decimal CurrentPrice { get; set; }
    
    // 5. Public methods
    public async Task<Result> PublicMethodAsync() { }
    
    // 6. Private methods
    private decimal CalculateValue() { }
}
```

#### Documentation Requirements
```csharp
/// <summary>
/// Calculates put spread pricing using real market data
/// </summary>
/// <param name="symbol">Trading symbol (e.g., "SOFI")</param>
/// <param name="stockPrice">Current underlying price</param>
/// <param name="shortStrike">Short put strike (higher price)</param>
/// <returns>Pricing information with Greeks and risk metrics</returns>
public async Task<RealOptionsPricing> GetPutSpreadPricing(
    string symbol, 
    decimal stockPrice, 
    decimal shortStrike)
```

### Error Handling Standards

```csharp
// Use structured result objects
public class OperationResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string ErrorMessage { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
}

// Example usage
public async Task<OperationResult<RealOptionsPricing>> GetPricingAsync()
{
    try
    {
        var pricing = await CalculatePricing();
        return new OperationResult<RealOptionsPricing>
        {
            Success = true,
            Data = pricing
        };
    }
    catch (Exception ex)
    {
        return new OperationResult<RealOptionsPricing>
        {
            Success = false,
            ErrorMessage = ex.Message
        };
    }
}
```

### Performance Guidelines

1. **Use async/await properly:**
   ```csharp
   // Good
   public async Task<decimal> GetPriceAsync()
   {
       var result = await _client.GetDataAsync();
       return result.Price;
   }
   
   // Avoid blocking calls
   // Bad: .Result or .Wait()
   ```

2. **Dispose resources:**
   ```csharp
   using var client = new HttpClient();
   // Resource automatically disposed
   ```

3. **Use efficient collections:**
   ```csharp
   // Use appropriate collection types
   var lookup = items.ToLookup(x => x.Symbol);  // For grouping
   var hashSet = new HashSet<string>();         // For uniqueness
   ```

## Testing Requirements

### Test Structure

Tests should follow the AAA pattern:
```csharp
[Fact]
public async Task CalculatePositionSize_WithHighVix_ShouldReduceSize()
{
    // Arrange
    var riskManager = new AdvancedRiskManager();
    var config = CreateTestSymbolConfig();
    var highVix = 35.0m;
    
    // Act
    var result = riskManager.CalculateOptimalPositionSize(
        "SOFI", 50000m, highVix, new PortfolioPnL(), config);
    
    // Assert
    result.RecommendedSize.Should().BeLessThan(5000m);
    result.RiskLevel.Should().Be(RiskLevel.Low);
}
```

### Test Categories

1. **Unit Tests** (`src/Sofired.Tests/Core/`):
   - Test individual components in isolation
   - Use mocking for dependencies
   - Fast execution (<1 second per test)

2. **Integration Tests** (`src/Sofired.Tests/Integration/`):
   - Test component interactions
   - Use real dependencies where appropriate
   - Longer execution time acceptable

3. **Performance Tests** (`src/Sofired.Tests/Performance/`):
   - Benchmark critical operations
   - Memory usage validation
   - Execution time limits

### Test Requirements

- **Coverage**: Minimum 80% code coverage for new code
- **Naming**: Descriptive test names with Given/When/Then pattern
- **Data**: Use realistic test data
- **Assertions**: Use FluentAssertions for readable tests
- **Isolation**: Tests should be independent and repeatable

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "Category=Unit"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run performance tests
dotnet test src/Sofired.Tests/Performance/
```

## Documentation Guidelines

### Code Documentation

- **XML Comments**: All public APIs must have XML documentation
- **README Updates**: Update relevant README sections for new features
- **Architecture Docs**: Update architecture diagrams for structural changes
- **API Reference**: Update API documentation for new endpoints

### Documentation Standards

1. **Be Clear and Concise**: Use simple language
2. **Include Examples**: Provide usage examples
3. **Keep Updated**: Maintain documentation with code changes
4. **Use Proper Formatting**: Follow Markdown standards

### Required Documentation

For new features, include:
- API documentation updates
- Configuration examples
- Usage examples in README
- Architecture updates if applicable

## Pull Request Process

### Before Submitting

1. **Self-Review**: Review your own changes thoroughly
2. **Test Locally**: Run all tests and ensure they pass
3. **Documentation**: Update relevant documentation
4. **Rebase**: Rebase against latest develop branch
5. **Commit Messages**: Use conventional commit format

### Pull Request Template

Use this template for your PR description:

```markdown
## Description
Brief description of changes made

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] This change requires a documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] All tests pass locally
- [ ] Performance impact assessed

## Documentation
- [ ] Code comments updated
- [ ] README updated
- [ ] API documentation updated
- [ ] Architecture docs updated (if applicable)

## Checklist
- [ ] My code follows the style guidelines
- [ ] I have performed a self-review
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] My changes generate no new warnings
- [ ] New and existing unit tests pass locally
- [ ] Any dependent changes have been merged and published
```

### Review Process

1. **Automated Checks**: CI/CD pipeline runs tests and linting
2. **Code Review**: At least one maintainer review required
3. **Testing**: Reviewer validates functionality
4. **Approval**: Approved PRs are merged by maintainers

### Addressing Feedback

- **Be Responsive**: Address feedback promptly
- **Ask Questions**: Clarify unclear feedback
- **Make Changes**: Update code based on suggestions
- **Test Again**: Ensure changes don't break functionality

## Issue Reporting

### Bug Reports

Use the bug report template:

```markdown
## Bug Description
Clear description of the bug

## Steps to Reproduce
1. Step one
2. Step two
3. Step three

## Expected Behavior
What should have happened

## Actual Behavior
What actually happened

## Environment
- OS: [e.g., Windows 11]
- .NET Version: [e.g., 8.0]
- SOFIRED Version: [e.g., 5.0]

## Additional Context
Any other relevant information
```

### Feature Requests

Use the feature request template:

```markdown
## Feature Description
Clear description of the proposed feature

## Business Case
Why is this feature needed?

## Proposed Solution
How should this be implemented?

## Alternatives Considered
Other approaches that were considered

## Additional Context
Any other relevant information
```

### Issue Labels

We use these labels for issue categorization:
- `bug`: Something isn't working
- `enhancement`: New feature or request
- `documentation`: Improvements to documentation
- `good first issue`: Good for newcomers
- `help wanted`: Extra attention is needed
- `priority:high`: High priority issue
- `priority:low`: Low priority issue

## Release Process

### Versioning

We follow Semantic Versioning (SemVer):
- `MAJOR.MINOR.PATCH`
- **MAJOR**: Breaking changes
- **MINOR**: New features, backward compatible
- **PATCH**: Bug fixes, backward compatible

### Release Workflow

1. **Feature Freeze**: No new features for release
2. **Testing**: Comprehensive testing of release candidate
3. **Documentation**: Update all relevant documentation
4. **Tagging**: Create release tag with version number
5. **Release Notes**: Document all changes and improvements

## Community Guidelines

### Getting Help

- **Documentation**: Check docs folder first
- **GitHub Discussions**: Ask questions and discuss ideas
- **Issues**: Report bugs and request features
- **Code Reviews**: Participate in reviewing PRs

### Recognition

We recognize contributors through:
- **Contributors List**: Listed in README
- **Release Notes**: Mentioned in release notes
- **Special Recognition**: Outstanding contributions highlighted

## Development Tips

### Recommended Tools

- **Visual Studio Code** with C# extension
- **Git GUI** tools like SourceTree or GitKraken
- **Postman** for API testing
- **dotMemory** or **PerfView** for performance analysis

### Useful Commands

```bash
# Clean and rebuild
dotnet clean && dotnet build

# Run with specific configuration
dotnet run --project src/Sofired.Backtester --configuration Release

# Watch for changes during development
dotnet watch run --project src/Sofired.Core

# Generate code coverage report
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
```

### IDE Configuration

Recommended VS Code settings:
```json
{
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.organizeImports": true
    },
    "dotnet.completion.showCompletionItemsFromUnimportedNamespaces": true
}
```

---

Thank you for contributing to SOFIRED! Your contributions help make this project better for everyone. If you have questions about contributing, please don't hesitate to ask in GitHub Discussions or create an issue.

**Happy Coding!** 🚀