# ObjectIR .NET Importer – Top 20 Todo List

## Phase 1: Core IL Coverage (Highest Priority)

- [ ] Add support for `stloc`, `stloc.0-3`, `stloc.s`
- [ ] Add support for `ldloc`, `ldloc.0-3`, `ldloc.s`
- [ ] Add support for `ldarg`, `ldarg.0-3`, `ldarg.s`
- [ ] Add support for `starg`
- [ ] Add support for `dup`
- [ ] Add support for `pop`

---

## Phase 2: Branching / Control Flow

- [ ] Add support for `br`, `br.s`
- [ ] Add support for `brtrue`, `brfalse`
- [ ] Add support for `beq`, `bne.un`
- [ ] Add support for `blt`, `ble`, `bgt`, `bge`
- [ ] Build label system for branch targets
- [ ] Reconstruct `if / else`
- [ ] Reconstruct `while / for` loops

---

## Phase 3: Arrays + Memory

- [ ] Add support for `newarr`
- [ ] Add support for `ldlen`
- [ ] Add support for `ldelem.*`
- [ ] Add support for `stelem.*`
- [ ] Add support for `ldfld`, `stfld`
- [ ] Add support for `ldsfld`, `stsfld`

---

## Phase 4: Type Mapping

- [ ] Map `System.Boolean` -> `bool`
- [ ] Map `System.String` -> `string`
- [ ] Map `System.Object` -> `object`
- [ ] Map arrays like `System.Int32[]`
- [ ] Map generic types like `List<T>`

---

## Phase 5: Calls / Methods

- [ ] Improve instance method call detection
- [ ] Improve static method call detection
- [ ] Detect constructors (`newobj`)
- [ ] Handle overload signatures properly
- [ ] Handle virtual/interface dispatch

---

## Phase 6: Cleanup / Quality

- [ ] Remove fake `void` locals
- [ ] Improve local variable naming (`local0`, `sum`, etc.)
- [ ] Skip compiler-generated methods if desired
- [ ] Detect properties (`get_`, `set_`)
- [ ] Detect async/iterator generated code

---

## Phase 7: Advanced Compiler Features

- [ ] Build control-flow graph (CFG)
- [ ] Add SSA temporary generation
- [ ] Constant folding pass
- [ ] Dead code elimination
- [ ] Pretty printer for readable ObjectIR

---

## Phase 8: Stretch Goals

- [ ] Import Unity assemblies
- [ ] Import VB.NET assemblies
- [ ] Import F# assemblies
- [ ] Export back to C#
- [ ] Self-host ObjectIR importer

---

# Immediate Next 5 (Recommended)

1. `ldloc / stloc`
2. `brtrue / brfalse`
3. `newarr / ldelem / stelem`
4. Better type mapping
5. Loop reconstruction
