# .NET 10 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 10 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10 upgrade.
3. Upgrade Shared.csproj to .NET 10
4. Upgrade SmartFlow.UI.Client.csproj to .NET 10
5. Upgrade SmartFlow.UI.API.csproj to .NET 10

## Settings

This section contains settings and data used by execution steps.

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                                              | Current Version | New Version | Description                                   |
|:----------------------------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| Azure.Identity                                            | 1.17.0          | 1.17.1      | Package is deprecated                         |
| Microsoft.AspNetCore.Components.WebAssembly               | 8.0.14          | 10.0.0      | Recommended for .NET 10                       |
| Microsoft.AspNetCore.Components.WebAssembly.Server        | 8.0.20          | 10.0.0      | Recommended for .NET 10                       |
| Microsoft.Extensions.Azure                                | 1.13.0          | 1.13.1      | Package is deprecated                         |
| Microsoft.Extensions.Configuration.Abstractions           | 9.0.3           | 10.0.0      | Recommended for .NET 10                       |
| Microsoft.Extensions.Http                                 | 9.0.3           | 10.0.0      | Recommended for .NET 10                       |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets     | 1.22.1          |             | No supported version found - needs review     |
| Newtonsoft.Json                                           | 13.0.3          | 13.0.4      | Recommended update                            |
| System.Text.Json                                          | 9.0.4           | 10.0.0      | Recommended for .NET 10                       |

### Project upgrade details

This section contains details about each project upgrade and modifications that need to be done in the project.

#### Shared.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - Microsoft.Extensions.Configuration.Abstractions should be updated from `9.0.3` to `10.0.0` (*recommended for .NET 10*)
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended update*)

#### SmartFlow.UI.Client.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - Microsoft.AspNetCore.Components.WebAssembly should be updated from `8.0.14` to `10.0.0` (*recommended for .NET 10*)
  - Microsoft.Extensions.Http should be updated from `9.0.3` to `10.0.0` (*recommended for .NET 10*)
  - System.Text.Json should be updated from `9.0.4` to `10.0.0` (*recommended for .NET 10*)
  - Newtonsoft.Json should be updated from `13.0.3` to `13.0.4` (*recommended update*)

#### SmartFlow.UI.API.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - Microsoft.AspNetCore.Components.WebAssembly.Server should be updated from `8.0.20` to `10.0.0` (*recommended for .NET 10*)
  - Azure.Identity should be updated from `1.17.0` to `1.17.1` (*package is deprecated*)
  - Microsoft.Extensions.Azure should be updated from `1.13.0` to `1.13.1` (*package is deprecated*)

Other changes:
  - Microsoft.VisualStudio.Azure.Containers.Tools.Targets package (version 1.22.1) has no supported version for .NET 10 and needs to be reviewed manually after upgrade
