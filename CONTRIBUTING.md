# Contributing

Thank you for contributing to AIDrivenFramework.

## Test suites

The package has two kinds of tests:

- `AIDrivenFW.Unit.Tests` contains isolated Edit Mode tests. They use test doubles and do not require a local LLM runtime or model.
- Integration and end-to-end tests exercise an actual AI runtime. Run them only when a compatible local runtime and model are available.

Before opening a pull request, import the package into a Unity 6 project, open **Window > General > Test Runner**, select **EditMode**, and run the `AIDrivenFW.Unit.Tests` assembly. The same assembly can be selected on the command line with `-assemblyNames AIDrivenFW.Unit.Tests`; see Unity's [Test Framework command-line reference](https://docs.unity3d.com/Packages/com.unity.test-framework@2.0/manual/reference-command-line.html).

## GitHub Actions setup

The Unity unit-test workflow uses [GameCI's Unity Test Runner](https://game.ci/docs/github/test-runner/) in package mode with Unity `6000.0.60f1`. Repository maintainers must configure these GitHub Actions secrets for a Unity Personal license:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

Follow GameCI's [activation guide](https://game.ci/docs/github/activation/) to create the license value and store all three values as repository secrets. Pull requests from forks skip the Unity job because GitHub does not expose repository secrets to fork workflows.

The workflow resolves `com.cysharp` and `com.annulusgames` packages through the [OpenUPM registry](https://openupm.com/docs/getting-started.html#scoped-registry). If dependency scopes change in `package.json`, update `registryScopes` in `.github/workflows/unity-unit-tests.yml` as well.
