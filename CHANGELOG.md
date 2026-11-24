# Changelog

## 3.0.1 - 2026-11-24

- Version unification across the N2 packages: Version/PackageVersion 3.0.1, FileVersion 3.0.1.0,
  AssemblyVersion 3.0.0.0.
- Target frameworks: net10.0 and net11.0 (unchanged). netstandard2.0 was evaluated and left out:
  the code uses TimeProvider, System.Threading.Channels and init-only records, which need
  polyfills or source changes.
- Dependencies checked: all packages are at their current stable version for each target
  framework. N2.Core.Abstractions stays at 3.0.0 until 3.0.1 is published.
- Publishing to vault-packages moved into the GitHub Actions workflow (push to trunk,
  --skip-duplicate); the Azure Pipelines pipeline is no longer needed.
