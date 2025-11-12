# .NET 10 Upgrade Report

## Project target framework modifications

| Project name                                   | Old Target Framework    | New Target Framework         | Commits                   |
|:-----------------------------------------------|:-----------------------:|:----------------------------:|---------------------------|
| mvsep-cli.csproj                                |   net9.0                | net10.0                      | af27871f, ed66c44c        |

## NuGet Packages

| Package Name                        | Old Version | New Version | Commit Id                                 |
|:------------------------------------|:-----------:|:-----------:|-------------------------------------------|
| System.Text.Json                    |   9.0.10    |  10.0.0     | 221e1ac3                                  |

## All commits

| Commit ID              | Description                                |
|:-----------------------|:-------------------------------------------|
| af27871f               | Update mvsep-cli.csproj to target .NET 10.0 |
| ed66c44c               | Commit upgrade plan                         |
| 221e1ac3               | Update System.Text.Json to v10.0.0          |
| 50481645               | Remove redundant System.Text.Json reference |

## Project feature upgrades

### mvsep-cli.csproj

- Target framework updated to `net10.0`.
- System.Text.Json package reference updated to `10.0.0` and then removed as it is provided by the platform.

## Next steps

- Build and run tests (if present) to validate the upgrade.
