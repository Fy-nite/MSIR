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


### Why ObjectIR?

Most compiler toolkits target low-level bytecode (too far from OO semantics) or tie you to a specific runtime. ObjectIR gives you a strongly-typed in-memory graph of your program's types and methods that you can build with a fluent API, compose across modules, and serialize to multiple formats.

## Usage

```bash
# Transpile an assembly to ObjectIR text format
dotnet run --project MSIR -- path/to/assembly.dll > output.oir
```

## License

MIT — see [LICENCE](LICENCE). Copyright 2026 Finite R&D.
