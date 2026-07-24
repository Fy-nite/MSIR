# MSIR — CIL to ObjectIR Transpiler

Reads .NET assemblies via [Mono.Cecil](https://github.com/jbevain/cecil) and translates them into **ObjectIR**, an object-oriented intermediate representation designed for analysis, transformation, and cross-language code generation.

## Overview

```
.NET assembly (.dll/.exe)
       │
    MSIR (VB.NET)
       │  uses Mono.Cecil to read IL
       ▼
   ObjectIR module
       │
       ├── Text IR format (.oir)
       ├── JSON / BSON
       └── Compact FOB binary (.fob)
```

MSIR translates CIL bytecode into ObjectIR's structured control-flow IR (if/else, while, for) instead of raw branch labels, making the output easier to work with for downstream tools.

## Repository Structure

```
MSIR/
├── MSIR/                      # VB.NET transpiler (entry point)
│   └── Program.vb             # Reads assembly → builds ObjectIR module
├── libs/
│   └── ObjectIR.Core/         # C# library: IR model, builder, serialization
│       ├── IR/                # Core data model (Module, TypeDefinition, Instructions)
│       ├── Builder/           # Fluent IRBuilder API
│       ├── Serialization/     # Text, JSON, BSON serializers + ModuleLoader
│       ├── Composition/       # ModuleComposer, DependencyResolver
│       ├── Compilers/         # Construct-language front-end compiler
│       ├── Core/              # AST parsing, Value/Node types
│       └── docs/              # Full documentation
└── MSIRTest/                  # C# test project
    ├── Program.cs
    └── IO.cs                  # Stub classes for transpiler output
```

## ObjectIR.Core

The core library is also available as a NuGet package:

```bash
dotnet add package ObjectIR.Core
```

Build modules programmatically:

```csharp
var module = new IRBuilder("MyApp")
    .Class("Animal")
        .Field("name", TypeReference.String)
            .Access(AccessModifier.Private)
            .EndField()
        .Method("Speak", TypeReference.String)
            .Virtual()
            .Body()
                .Ldarg(0)
                .Ldfld(new FieldReference(
                    TypeReference.FromName("Animal"),
                    "name",
                    TypeReference.String))
                .Ret()
            .EndBody()
        .EndMethod()
    .EndClass()
    .Build();
```

### Why ObjectIR?

Most compiler toolkits target low-level bytecode (too far from OO semantics) or tie you to a specific runtime. ObjectIR gives you a strongly-typed in-memory graph of your program's types and methods that you can build with a fluent API, compose across modules, and serialize to multiple formats.

## Usage

```bash
# Transpile an assembly to ObjectIR text format
dotnet run --project MSIR -- path/to/assembly.dll > output.oir
```

## Documentation

Full docs for ObjectIR.Core live in `libs/ObjectIR.Core/docs/`:

| Page | Description |
|------|-------------|
| [Getting Started](libs/ObjectIR.Core/docs/getting-started.md) | Install and build your first module |
| [Architecture](libs/ObjectIR.Core/docs/architecture.md) | Design and namespace layout |
| [IR Model](libs/ObjectIR.Core/docs/ir-model.md) | Data model reference |
| [Builder API](libs/ObjectIR.Core/docs/builder-api.md) | Fluent builder walkthrough |
| [Serialization](libs/ObjectIR.Core/docs/serialization.md) | Load and save modules |
| [Composition](libs/ObjectIR.Core/docs/composition.md) | Merging modules |
| [FOB Format](libs/ObjectIR.Core/docs/fob-format.md) | Compact binary format spec |

## License

MIT — see [LICENCE](LICENCE). Copyright 2026 Finite R&D.
