# TODO

Status: pending explicit instruction before implementation.

## meshoptimizer native integration

1. **Locate native source** (`native-source-location`)
   - Identify the authoritative source repository or local project that produces `Assets\Editor\MeshOptimizer\Plugins\meshoptimizer.dll`.
   - Confirm whether the DLL comes from upstream meshoptimizer directly or from a custom wrapper project.

2. **Define native build** (`native-build-command`)
   - Capture the exact Windows build steps required to rebuild `meshoptimizer.dll`, `meshoptimizer.lib`, and `meshoptimizer.pdb`.
   - Record required toolchain and expected outputs.

3. **Automate plugin copy** (`plugin-copy-step`)
   - Define or add the integration step that copies rebuilt native outputs into `Assets\Editor\MeshOptimizer\Plugins`.
   - Preserve the current Unity plugin layout and importer expectations.

4. **Document plugin contract** (`plugin-platform-contract`)
   - Document exported symbols, `CallingConvention.Cdecl`, and buffer / struct layout assumptions.
   - Note the constraints for changing `MeshOptimizerNative.cs` and `MeshOperations.cs`.

## SQL todo mirror

| Status | ID | Title |
| --- | --- | --- |
| pending | `native-source-location` | Locate native source |
| pending | `native-build-command` | Define native build |
| pending | `plugin-copy-step` | Automate plugin copy |
| pending | `plugin-platform-contract` | Document plugin contract |
