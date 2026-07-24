Imports System
Imports System.Reflection
Imports Mono.Cecil
Imports System.Linq
Imports Mono.Collections.Generic
Imports ObjectIR.Core.Builder
Imports ObjectIR.Core.IR
Imports ObjectIR.Core.Serialization
Imports ObjectIR.Core.Serialization.AdvancedModuleFormats
Imports Mono.Cecil.Cil
Module Program
    Sub Main(args As String())
        Dim CurrentAssembly As String
        If args.Length > 0 Then
            CurrentAssembly = args(0)
        Else
            Console.WriteLine("Please provide the path to the assembly to process as a command-line argument.")
            Return
        End If
        Dim ASM = AssemblyDefinition.ReadAssembly(CurrentAssembly)
        Dim IRbuilder As New IRBuilder(ASM.Name.Name.ToString())
        Dim Types As String() = {"<Module>", "My", "Settings", "InternalXmlHelper", "Resources"} ' add more types to this list as needed to exclude from processing (e.g. auto-generated types or ones that cause issues in IR generation)
        For Each Type In ASM.MainModule.Types
            For Each b In Types
                If Type.Name.Contains(b) Then
                    Console.Error.WriteLine($"Not Processing Type: {Type.FullName}")
                    Continue For
                End If
            Next
            ProcessType(Type, IRbuilder)
        Next

        Dim built = IRbuilder.Build()
        ' Debug: inspect built IR for method static flags
        For Each t In built.Classes
            For Each m In t.Methods
                Console.Error.WriteLine($"IR DEBUG: Class {t.Name} Method {m.Name} IsStatic={m.IsStatic}")
            Next
        Next
        Console.WriteLine(built.Serialize().DumpToIRCode())
    End Sub

    Private Sub ProcessType(t As Mono.Cecil.TypeDefinition, builder As IRBuilder)
        Dim ClassBuilder = builder.Class(t.Name)

        ' Fields first
        AddTypeFields(t, ClassBuilder)

        ' Methods
        For Each m In t.Methods.OrderBy(Function(mt) mt.MetadataToken.ToInt32())
            ProcessMethod(m, ClassBuilder)
        Next
    End Sub

    Private Sub AddTypeFields(t As Mono.Cecil.TypeDefinition, classBuilder As ObjectIR.Core.Builder.ClassBuilder)
        For Each FieldDef In t.Fields.OrderBy(Function(f) f.MetadataToken.ToInt32())
            Dim fldTypeName = GetTypeName(FieldDef.FieldType)
            Dim fb = classBuilder.Field(FieldDef.Name, fldTypeName)
            If FieldDef.IsStatic Then fb.Static()
            fb.EndField()
        Next
    End Sub

    Private Sub ProcessMethod(mdef As Mono.Cecil.MethodDefinition, classBuilder As ObjectIR.Core.Builder.ClassBuilder)
        Dim MethodBuilder As ObjectIR.Core.Builder.MethodBuilder
        MethodBuilder = classBuilder.Method(mdef.Name, GetTypeName(mdef.ReturnType))

        If mdef.IsStatic Then MethodBuilder.Static()
        If mdef.IsAbstract Then MethodBuilder.Abstract()

        AddMethodParameters(mdef, MethodBuilder)
        AddMethodLocals(mdef, MethodBuilder)
        BuildInstructions(mdef, MethodBuilder)
    End Sub

    Private Sub AddMethodParameters(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        For Each p In mdef.Parameters
            Dim pTypeName = GetTypeName(p.ParameterType)
            mb.Parameter(If(String.IsNullOrEmpty(p.Name), "arg" & p.Index.ToString(), p.Name), pTypeName)
        Next
    End Sub

    Private Sub AddMethodLocals(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        If mdef.HasBody AndAlso mdef.Body IsNot Nothing Then
            For i As Integer = 0 To mdef.Body.Variables.Count - 1
                Dim v = mdef.Body.Variables(i)
                Dim FieldTypeName = GetTypeName(v.VariableType)
                Dim localName As String = GetVariableFriendlyName(v)
                mb.Local(localName, FieldTypeName)
            Next
        End If
    End Sub

    Private Function GetVariableFriendlyName(v As Mono.Cecil.Cil.VariableDefinition) As String
        Return "loc" & v.Index.ToString()
    End Function

    Private Sub BuildInstructions(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        Dim Instructions = mb.Body()
        If Not (mdef.HasBody AndAlso mdef.Body IsNot Nothing) Then
            mb.EndMethod()
            Return
        End If
        ' Parse method body normally. Don't treat op_Equality specially here;
        ' let the instruction-level parser map calls and branches into control flow.
        ParseInstructions(mdef, mb.Body())
        mb.EndMethod()
    End Sub

    Private Sub ParseInstructions(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.InstructionBuilder, Optional startIndex As Integer = 0, Optional endIndex As Integer = -1)
        Dim instr = mdef.Body.Instructions
        Dim instructions = mb
        If endIndex = -1 Then endIndex = instr.Count
        Dim i As Integer = startIndex
        While i < endIndex
            Dim Instructionz = instr(i)
            Select Case Instructionz.OpCode.Code
                Case Mono.Cecil.Cil.Code.Call, Mono.Cecil.Cil.Code.Callvirt
                    Dim MethodRef = TryCast(Instructionz.Operand, Mono.Cecil.MethodReference)
                    If MethodRef IsNot Nothing Then
                        ' Convert equality operator calls to a compare-equal instruction
                        If MethodRef.Name = "op_Equality" AndAlso GetTypeName(MethodRef.ReturnType) = "System.Boolean" Then
                            ' op_Equality returns bool -> emit compare-equal instead of call
                            instructions.Ceq()
                        Else
                            ' Create AST MethodReference
                            Dim declType = GetTypeName(MethodRef.DeclaringType)
                            Dim retType = GetTypeName(MethodRef.ReturnType)
                            Dim paramTypes As New List(Of ObjectIR.Core.AST.TypeRef)
                            For Each p In MethodRef.Parameters
                                paramTypes.Add(GetTypeName(p.ParameterType))
                            Next

                            Dim astMethodRef = TranslateStdMethod(declType, MethodRef.Name, retType, paramTypes)

                            If Instructionz.OpCode.Code = Mono.Cecil.Cil.Code.Callvirt Then
                                instructions.Callvirt(astMethodRef)
                            Else
                                instructions.Call(astMethodRef)
                            End If
                        End If
                    End If
                Case Code.Nop
                    ' ignore NOPs
                Case Code.Ret
                    instructions.Ret()
                Case Code.Dup
                    instructions.Dup()
                Case Code.Pop
                    instructions.Pop()
                Case Code.Newobj
                    Dim ctorRef = TryCast(Instructionz.Operand, Mono.Cecil.MethodReference)
                    If ctorRef IsNot Nothing Then
                        Dim ty = GetTypeName(ctorRef.DeclaringType)
                        instructions.Newobj(ty)
                    End If
                Case Code.Newarr
                    Dim arrType = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If arrType IsNot Nothing Then
                        instructions.Newarr(GetTypeName(arrType))
                    End If
                Case Code.Ldnull
                    instructions.Ldnull()
                Case Code.Ceq
                    instructions.Ceq()
                Case Code.Clt, Code.Clt_Un
                    instructions.Clt()
                Case Code.Cgt, Code.Cgt_Un
                    instructions.Cgt()
                Case Code.Ldarg, Code.Ldarg_S, Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3
                    Dim argName As String = Nothing
                    If Instructionz.OpCode.Code = Code.Ldarg_0 Then
                        argName = GetArgFriendlyName(mdef, 0)
                    ElseIf Instructionz.OpCode.Code = Code.Ldarg_1 Then
                        argName = GetArgFriendlyName(mdef, 1)
                    ElseIf Instructionz.OpCode.Code = Code.Ldarg_2 Then
                        argName = GetArgFriendlyName(mdef, 2)
                    ElseIf Instructionz.OpCode.Code = Code.Ldarg_3 Then
                        argName = GetArgFriendlyName(mdef, 3)
                    ElseIf Instructionz.Operand IsNot Nothing Then
                        Dim pd = TryCast(Instructionz.Operand, Mono.Cecil.ParameterDefinition)
                        If pd IsNot Nothing Then
                            argName = If(String.IsNullOrEmpty(pd.Name), "arg" & pd.Index.ToString(), pd.Name)
                        Else
                            argName = "arg" & Instructionz.Operand.ToString()
                        End If
                    End If
                    If Not String.IsNullOrEmpty(argName) Then
                        instructions.Ldarg(argName)
                    End If
                Case Code.Starg, Code.Starg_S
                    Dim argName As String = Nothing
                    Dim pd = TryCast(Instructionz.Operand, Mono.Cecil.ParameterDefinition)
                    If pd IsNot Nothing Then
                        argName = If(String.IsNullOrEmpty(pd.Name), "arg" & pd.Index.ToString(), pd.Name)
                    Else
                        argName = "arg" & Instructionz.Operand.ToString()
                    End If
                    If Not String.IsNullOrEmpty(argName) Then
                        instructions.Starg(argName)
                    End If
                Case Code.Ldarga, Code.Ldarga_S
                    Dim argName As String = Nothing
                    Dim pd = TryCast(Instructionz.Operand, Mono.Cecil.ParameterDefinition)
                    If pd IsNot Nothing Then
                        argName = If(String.IsNullOrEmpty(pd.Name), "arg" & pd.Index.ToString(), pd.Name)
                    Else
                        argName = "arg" & Instructionz.Operand.ToString()
                    End If
                    If Not String.IsNullOrEmpty(argName) Then
                        instructions.Ldarga(argName)
                    End If
                Case Code.Ldstr
                    Dim Str = TryCast(Instructionz.Operand, String)
                    If Str IsNot Nothing Then
                        instructions.Ldstr(Str)
                    End If
                Case Code.Castclass
                    Dim ty = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If ty IsNot Nothing Then
                        instructions.Castclass(GetTypeName(ty))
                    End If
                Case Code.Isinst
                    Dim ty = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If ty IsNot Nothing Then
                        instructions.Isinst(GetTypeName(ty))
                    End If
                Case Code.Box
                    Dim ty = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If ty IsNot Nothing Then
                        instructions.Box(GetTypeName(ty))
                    End If
                Case Code.Unbox, Code.Unbox_Any
                    Dim ty = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If ty IsNot Nothing Then
                        instructions.Unbox(GetTypeName(ty))
                    End If
                Case Code.Ldc_I4, Code.Ldc_I4_S, Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3, Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7, Code.Ldc_I4_8
                    Dim value As Integer
                    If Instructionz.OpCode.Code = Code.Ldc_I4_S Then
                        value = Convert.ToInt32(Instructionz.Operand)
                    ElseIf Instructionz.OpCode.Code >= Code.Ldc_I4_0 AndAlso Instructionz.OpCode.Code <= Code.Ldc_I4_8 Then
                        value = Instructionz.OpCode.Code - Code.Ldc_I4_0
                    Else
                        value = Convert.ToInt32(Instructionz.Operand)
                    End If
                    instructions.LdcI4(value)
                Case Code.Ldc_I8
                    instructions.LdcI8(Convert.ToInt64(Instructionz.Operand))
                Case Code.Ldc_R4
                    instructions.LdcR4(Convert.ToSingle(Instructionz.Operand))
                Case Code.Ldc_R8
                    instructions.LdcR8(Convert.ToDouble(Instructionz.Operand))
                Case Code.Add
                    instructions.Add()
                Case Code.Sub
                    instructions.Sub()
                Case Code.Mul
                    instructions.Mul()
                Case Code.Div
                    instructions.Div()
                Case Code.[Rem]
                    instructions.[Rem]()
                Case Code.Neg
                    instructions.Neg()
                Case Code.And
                    instructions.And()
                Case Code.Or
                    instructions.Or()
                Case Code.Xor
                    instructions.Xor()
                Case Code.Not
                    instructions.Not()
                Case Code.Shl
                    instructions.Shl()
                Case Code.Shr, Code.Shr_Un
                    instructions.Shr()
                Case Code.Ldfld, Code.Ldsfld
                    Dim FieldRef = TryCast(Instructionz.Operand, Mono.Cecil.FieldReference)
                    If FieldRef IsNot Nothing Then
                        Dim fr = New ObjectIR.Core.AST.FieldReference(GetTypeName(FieldRef.DeclaringType), FieldRef.Name, GetTypeName(FieldRef.FieldType))
                        If Instructionz.OpCode.Code = Code.Ldfld Then
                            instructions.Ldfld(fr)
                        Else
                            instructions.Ldsfld(fr)
                        End If
                    End If
                Case Code.Stfld, Code.Stsfld
                    Dim FieldRef = TryCast(Instructionz.Operand, Mono.Cecil.FieldReference)
                    If FieldRef IsNot Nothing Then
                        Dim fr = New ObjectIR.Core.AST.FieldReference(GetTypeName(FieldRef.DeclaringType), FieldRef.Name, GetTypeName(FieldRef.FieldType))
                        If Instructionz.OpCode.Code = Code.Stfld Then
                            instructions.Stfld(fr)
                        Else
                            instructions.Stsfld(fr)
                        End If
                    End If
                Case Code.Stloc, Code.Stloc_S, Code.Stloc_0, Code.Stloc_1, Code.Stloc_2, Code.Stloc_3
                    Dim localName As String = Nothing
                    If Instructionz.OpCode.Code = Code.Stloc_0 Then
                        localName = GetLocalFriendlyName(mdef, 0)
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_1 Then
                        localName = GetLocalFriendlyName(mdef, 1)
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_2 Then
                        localName = GetLocalFriendlyName(mdef, 2)
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_3 Then
                        localName = GetLocalFriendlyName(mdef, 3)
                    ElseIf Instructionz.Operand IsNot Nothing Then
                        Dim v = TryCast(Instructionz.Operand, Mono.Cecil.Cil.VariableDefinition)
                        If v IsNot Nothing Then
                            localName = GetVariableFriendlyName(v)
                        Else
                            localName = "loc" & Instructionz.Operand.ToString()
                        End If
                    End If
                    If Not String.IsNullOrEmpty(localName) Then
                        instructions.Stloc(localName)
                    End If
                Case Code.Ldloc, Code.Ldloc_S, Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3
                    Dim localName As String = Nothing
                    If Instructionz.OpCode.Code = Code.Ldloc_0 Then
                        localName = GetLocalFriendlyName(mdef, 0)
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_1 Then
                        localName = GetLocalFriendlyName(mdef, 1)
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_2 Then
                        localName = GetLocalFriendlyName(mdef, 2)
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_3 Then
                        localName = GetLocalFriendlyName(mdef, 3)
                    ElseIf Instructionz.Operand IsNot Nothing Then
                        Dim v = TryCast(Instructionz.Operand, Mono.Cecil.Cil.VariableDefinition)
                        If v IsNot Nothing Then
                            localName = GetVariableFriendlyName(v)
                        Else
                            localName = "loc" & Instructionz.Operand.ToString()
                        End If
                    End If
                    If Not String.IsNullOrEmpty(localName) Then
                        instructions.Ldloc(localName)
                    End If
                Case Code.Ldloca, Code.Ldloca_S
                    Dim localName As String = Nothing
                    Dim v = TryCast(Instructionz.Operand, Mono.Cecil.Cil.VariableDefinition)
                    If v IsNot Nothing Then
                        localName = GetVariableFriendlyName(v)
                    Else
                        localName = "loc" & Instructionz.Operand.ToString()
                    End If
                    instructions.Ldloca(localName)
                Case Code.Ldelem_Any, Code.Ldelem_I, Code.Ldelem_I1, Code.Ldelem_I2, Code.Ldelem_I4, Code.Ldelem_I8, Code.Ldelem_R4, Code.Ldelem_R8, Code.Ldelem_Ref, Code.Ldelem_U1, Code.Ldelem_U2, Code.Ldelem_U4
                    instructions.Ldelem()
                Case Code.Stelem_Any, Code.Stelem_I, Code.Stelem_I1, Code.Stelem_I2, Code.Stelem_I4, Code.Stelem_I8, Code.Stelem_R4, Code.Stelem_R8, Code.Stelem_Ref
                    instructions.Stelem()
                Case Code.Ldlen
                    instructions.Ldlen()
                Case Code.Ldftn, Code.Ldvirtftn
                    Dim mref = TryCast(Instructionz.Operand, Mono.Cecil.MethodReference)
                    If mref IsNot Nothing Then
                        Dim declType = GetTypeName(mref.DeclaringType)
                        Dim retType = GetTypeName(mref.ReturnType)
                        Dim paramTypes As New List(Of ObjectIR.Core.AST.TypeRef)
                        For Each p In mref.Parameters
                            paramTypes.Add(GetTypeName(p.ParameterType))
                        Next
                        Dim astMethodRef = New ObjectIR.Core.AST.MethodReference(declType, mref.Name, retType, paramTypes)
                        If Instructionz.OpCode.Code = Code.Ldftn Then
                            instructions.Ldftn(astMethodRef)
                        Else
                            instructions.Ldvirtftn(astMethodRef)
                        End If
                    End If
                Case Code.Ldtoken
                    instructions.Ldtoken(If(Instructionz.Operand IsNot Nothing, Instructionz.Operand.ToString(), ""))
                Case Code.Initobj
                    Dim ty = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If ty IsNot Nothing Then
                        instructions.Initobj(GetTypeName(ty))
                    End If
                Case Code.Throw, Code.Rethrow
                    instructions.Throw()
                Case Code.Conv_I1, Code.Conv_I2, Code.Conv_I4, Code.Conv_I, Code.Conv_Ovf_I1, Code.Conv_Ovf_I2, Code.Conv_Ovf_I4, Code.Conv_Ovf_I
                    instructions.ConvI4()
                Case Code.Conv_I8, Code.Conv_Ovf_I8
                    instructions.ConvI8()
                Case Code.Conv_R4
                    instructions.ConvR4()
                Case Code.Conv_R8
                    instructions.ConvR8()
                Case Code.Conv_U1, Code.Conv_U2, Code.Conv_U4, Code.Conv_U, Code.Conv_Ovf_U1, Code.Conv_Ovf_U2, Code.Conv_Ovf_U4, Code.Conv_Ovf_U
                    instructions.ConvU4()
                Case Code.Conv_U8, Code.Conv_Ovf_U8
                    instructions.ConvU8()
                Case Code.Brfalse, Code.Brfalse_S
                    Dim targetInstr = TryCast(Instructionz.Operand, Mono.Cecil.Cil.Instruction)
                    If targetInstr IsNot Nothing Then
                        Dim targetIndex = instr.IndexOf(targetInstr)
                        If targetIndex > i Then
                            ' Forward conditional branch: look for an else block pattern.
                            ' Pattern: [cond] brfalse L_else ; then-block ; br L_end ; L_else: else-block ; L_end:
                            Dim thenEndIndex = targetIndex
                            Dim thenHasTrailingBr As Boolean = False
                            Dim endIndexz As Integer = -1
                            If thenEndIndex - 1 >= 0 Then
                                ' Walk backwards skipping NOPs to find a trailing unconditional branch
                                Dim scan = thenEndIndex - 1
                                While scan >= 0 AndAlso instr(scan).OpCode.Code = Code.Nop
                                    scan -= 1
                                End While
                                If scan >= 0 Then
                                    Dim thenLast = instr(scan)
                                    If thenLast.OpCode.Code = Code.Br Or thenLast.OpCode.Code = Code.Br_S Then
                                        Dim endInstr = TryCast(thenLast.Operand, Mono.Cecil.Cil.Instruction)
                                        If endInstr IsNot Nothing Then
                                            endIndexz = instr.IndexOf(endInstr)
                                            If endIndexz > thenEndIndex Then
                                                thenHasTrailingBr = True
                                            End If
                                        End If
                                    End If
                                End If
                            End If

                            If thenHasTrailingBr AndAlso endIndexz > thenEndIndex Then
                                ' emit if with else
                                Dim cond = BacktrackCondition(mdef, i)
                                If cond = "stack" Then
                                    instructions.IfStack(Sub(thenBuilder)
                                                             ParseInstructions(mdef, thenBuilder, i + 1, thenEndIndex - 1)
                                                         End Sub,
                                                         Sub(elseBuilder)
                                                             ParseInstructions(mdef, elseBuilder, thenEndIndex, endIndexz)
                                                         End Sub)
                                Else
                                    instructions.If(cond, Sub(thenBuilder)
                                                              ParseInstructions(mdef, thenBuilder, i + 1, thenEndIndex - 1)
                                                          End Sub,
                                                          Sub(elseBuilder)
                                                              ParseInstructions(mdef, elseBuilder, thenEndIndex, endIndexz)
                                                          End Sub)
                                End If
                                ' Skip to endIndex
                                i = endIndexz - 1
                            Else
                                ' No else: simple if then-only
                                Dim cond = BacktrackCondition(mdef, i)
                                If cond = "stack" Then
                                    instructions.IfStack(Sub(thenBuilder)
                                                             ParseInstructions(mdef, thenBuilder, i + 1, targetIndex)
                                                         End Sub)
                                Else
                                    instructions.If(cond, Sub(thenBuilder)
                                                              ParseInstructions(mdef, thenBuilder, i + 1, targetIndex)
                                                          End Sub)
                                End If
                                i = targetIndex - 1
                            End If
                        Else
                            ' Backward branch (likely a loop) - fallback to emitting a comment or ignore for now
                            ' TODO: map backward branches to loop constructs
                        End If
                    End If
                Case Code.Br, Code.Br_S
                    ' Unconditional branch (goto) - for forward branches we can skip into target
                    Dim targetInstr2 = TryCast(Instructionz.Operand, Mono.Cecil.Cil.Instruction)
                    If targetInstr2 IsNot Nothing Then
                        Dim targetIndex2 = instr.IndexOf(targetInstr2)
                        If targetIndex2 > i Then
                            ' Skip forward to target (like a jump over a block)
                            i = targetIndex2 - 1
                        End If
                    End If
                Case Code.Brtrue, Code.Brtrue_S
                    Dim targetInstrT = TryCast(Instructionz.Operand, Mono.Cecil.Cil.Instruction)
                    If targetInstrT IsNot Nothing Then
                        Dim targetIndexT = instr.IndexOf(targetInstrT)
                        If targetIndexT > i Then
                            ' Pattern: brtrue L_then ; else-block (fallthrough) ; br L_end ; L_then: then-block
                            ' Try to detect trailing br at end of else-block that jumps to L_end
                            Dim elseStart = i + 1
                            Dim elseEndCandidate = targetIndexT
                            Dim elseHasTrailingBr As Boolean = False
                            Dim endIndexT As Integer = -1
                            If elseEndCandidate - 1 >= 0 Then
                                Dim scan2 = elseEndCandidate - 1
                                While scan2 >= 0 AndAlso instr(scan2).OpCode.Code = Code.Nop
                                    scan2 -= 1
                                End While
                                If scan2 >= 0 Then
                                    Dim elseLast = instr(scan2)
                                    If elseLast.OpCode.Code = Code.Br Or elseLast.OpCode.Code = Code.Br_S Then
                                        Dim endInstr2 = TryCast(elseLast.Operand, Mono.Cecil.Cil.Instruction)
                                        If endInstr2 IsNot Nothing Then
                                            endIndexT = instr.IndexOf(endInstr2)
                                            If endIndexT > elseEndCandidate Then
                                                elseHasTrailingBr = True
                                            End If
                                        End If
                                    End If
                                End If
                            End If

                            If elseHasTrailingBr AndAlso endIndexT > elseEndCandidate Then
                                ' emit if with else (else parsed first, then then-block)
                                Dim cond = BacktrackCondition(mdef, i)
                                instructions.If(cond, Sub(thenBuilder)
                                                          ParseInstructions(mdef, thenBuilder, targetIndexT, endIndexT)
                                                      End Sub,
                                                      Sub(elseBuilder)
                                                          ParseInstructions(mdef, elseBuilder, elseStart, elseEndCandidate - 1)
                                                      End Sub)
                                i = endIndexT - 1
                            Else
                                ' Fallback: treat as simple if with then at target (no else)
                                ' We'll emit the condition and then parse then-block
                                Dim cond = BacktrackCondition(mdef, i)
                                instructions.If(cond, Sub(thenBuilder)
                                                          ParseInstructions(mdef, thenBuilder, targetIndexT, instr.Count)
                                                      End Sub)
                                i = targetIndexT - 1
                            End If
                        End If
                    End If
            End Select
            i += 1
        End While

    End Sub

    Private Function TranslateStdMethod(declType As String, name As String, retType As String, paramTypes As List(Of ObjectIR.Core.AST.TypeRef)) As ObjectIR.Core.AST.MethodReference
        Dim d As String
        Dim n As String
        Select Case declType
            Case "System.Console"
                d = "IO"
            Case Else
                d = declType
        End Select
        Select Case name
            Case "WriteLine"
                n = "Println"
            Case Else
                n = name
        End Select
        Return New ObjectIR.Core.AST.MethodReference(d, n, retType, paramTypes)
    End Function

    Private Function GetArgFriendlyName(mdef As Mono.Cecil.MethodDefinition, index As Integer) As String
        If Not mdef.IsStatic AndAlso index = 0 Then Return "this"
        Dim realIndex = If(mdef.IsStatic, index, index - 1)
        If realIndex >= 0 AndAlso realIndex < mdef.Parameters.Count Then
            Dim p = mdef.Parameters(realIndex)
            Return If(String.IsNullOrEmpty(p.Name), "arg" & index.ToString(), p.Name)
        End If
        Return "arg" & index.ToString()
    End Function

    Private Function GetLocalFriendlyName(mdef As Mono.Cecil.MethodDefinition, index As Integer) As String
        If mdef.HasBody AndAlso mdef.Body IsNot Nothing AndAlso index < mdef.Body.Variables.Count Then
            Return GetVariableFriendlyName(mdef.Body.Variables(index))
        End If
        Return "loc" & index.ToString()
    End Function

    Private Function GetInstructionDescription(mdef As Mono.Cecil.MethodDefinition, instr As Mono.Cecil.Cil.Instruction) As String
        If instr Is Nothing Then Return "stack"
        Select Case instr.OpCode.Code
            Case Code.Ldloc, Code.Ldloc_S, Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3
                Return GetLocalName(mdef, instr)
            Case Code.Ldarg, Code.Ldarg_S, Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3
                Return GetArgName(mdef, instr)
            Case Code.Ldc_I4, Code.Ldc_I4_S, Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3, Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7, Code.Ldc_I4_8
                Return GetLdcI4Value(instr).ToString()
            Case Code.Ldnull
                Return "null"
            Case Code.Ldstr
                Return """" & instr.Operand.ToString() & """"
            Case Else
                Return "stack"
        End Select
    End Function

    Private Function GetLocalName(mdef As Mono.Cecil.MethodDefinition, instr As Mono.Cecil.Cil.Instruction) As String
        If instr.OpCode.Code = Code.Ldloc_0 Or instr.OpCode.Code = Code.Stloc_0 Then Return GetLocalFriendlyName(mdef, 0)
        If instr.OpCode.Code = Code.Ldloc_1 Or instr.OpCode.Code = Code.Stloc_1 Then Return GetLocalFriendlyName(mdef, 1)
        If instr.OpCode.Code = Code.Ldloc_2 Or instr.OpCode.Code = Code.Stloc_2 Then Return GetLocalFriendlyName(mdef, 2)
        If instr.OpCode.Code = Code.Ldloc_3 Or instr.OpCode.Code = Code.Stloc_3 Then Return GetLocalFriendlyName(mdef, 3)
        Dim v = TryCast(instr.Operand, Mono.Cecil.Cil.VariableDefinition)
        If v IsNot Nothing Then Return GetVariableFriendlyName(v)
        Return "loc" & If(instr.Operand IsNot Nothing, instr.Operand.ToString(), "?")
    End Function

    Private Function GetArgName(mdef As Mono.Cecil.MethodDefinition, instr As Mono.Cecil.Cil.Instruction) As String
        If instr.OpCode.Code = Code.Ldarg_0 Then Return GetArgFriendlyName(mdef, 0)
        If instr.OpCode.Code = Code.Ldarg_1 Then Return GetArgFriendlyName(mdef, 1)
        If instr.OpCode.Code = Code.Ldarg_2 Then Return GetArgFriendlyName(mdef, 2)
        If instr.OpCode.Code = Code.Ldarg_3 Then Return GetArgFriendlyName(mdef, 3)
        Dim pd = TryCast(instr.Operand, Mono.Cecil.ParameterDefinition)
        If pd IsNot Nothing Then Return If(String.IsNullOrEmpty(pd.Name), "arg" & pd.Index.ToString(), pd.Name)
        Return "arg" & If(instr.Operand IsNot Nothing, instr.Operand.ToString(), "?")
    End Function

    Private Function GetLdcI4Value(instr As Mono.Cecil.Cil.Instruction) As Integer
        If instr.OpCode.Code >= Code.Ldc_I4_0 AndAlso instr.OpCode.Code <= Code.Ldc_I4_8 Then
            Return instr.OpCode.Code - Code.Ldc_I4_0
        End If
        Return Convert.ToInt32(instr.Operand)
    End Function

    Private Function BacktrackCondition(mdef As Mono.Cecil.MethodDefinition, currentIndex As Integer) As String
        Dim instrs = mdef.Body.Instructions
        If currentIndex <= 0 Then Return "stack"

        ' Look back at the instruction that provided the condition
        Dim prev = instrs(currentIndex - 1)

        ' If it's a ldloc of a boolean temp, look back further to where it was stored
        If prev.OpCode.Code = Code.Ldloc Or prev.OpCode.Code = Code.Ldloc_S Or (prev.OpCode.Code >= Code.Ldloc_0 And prev.OpCode.Code <= Code.Ldloc_3) Then
            Dim localIdx = -1
            If prev.OpCode.Code = Code.Ldloc_0 Then
                localIdx = 0
            ElseIf prev.OpCode.Code = Code.Ldloc_1 Then
                localIdx = 1
            ElseIf prev.OpCode.Code = Code.Ldloc_2 Then
                localIdx = 2
            ElseIf prev.OpCode.Code = Code.Ldloc_3 Then
                localIdx = 3
            Else
                Dim v = TryCast(prev.Operand, Mono.Cecil.Cil.VariableDefinition)
                If v IsNot Nothing Then localIdx = v.Index
            End If

            If localIdx <> -1 Then
                ' Search backwards for the last stloc to this index
                For j As Integer = currentIndex - 2 To 0 Step -1
                    Dim scan = instrs(j)
                    Dim stIdx = -1
                    If scan.OpCode.Code = Code.Stloc_0 Then
                        stIdx = 0
                    ElseIf scan.OpCode.Code = Code.Stloc_1 Then
                        stIdx = 1
                    ElseIf scan.OpCode.Code = Code.Stloc_2 Then
                        stIdx = 2
                    ElseIf scan.OpCode.Code = Code.Stloc_3 Then
                        stIdx = 3
                    ElseIf scan.OpCode.Code = Code.Stloc Or scan.OpCode.Code = Code.Stloc_S Then
                        Dim v2 = TryCast(scan.Operand, Mono.Cecil.Cil.VariableDefinition)
                        If v2 IsNot Nothing Then stIdx = v2.Index
                    End If

                    If stIdx = localIdx Then
                        ' Found the store. What was on the stack before it?
                        Return BacktrackCondition(mdef, j)
                    End If
                Next
            End If
        End If

        ' Handle ldarg
        Dim argIdx = -1
        If prev.OpCode.Code = Code.Ldarg Or prev.OpCode.Code = Code.Ldarg_S Or (prev.OpCode.Code >= Code.Ldarg_0 And prev.OpCode.Code <= Code.Ldarg_3) Then
            If prev.OpCode.Code = Code.Ldarg_0 Then argIdx = 0
            'ElseIf prev.OpCode.Code = Code.Ldarg_1 Then argIdx = 1
            '        ElseIf prev.OpCode.Code = Code.Ldarg_2 Then argIdx = 2
            '        ElseIf prev.OpCode.Code = Code.Ldarg_3 Then argIdx = 3
        Else
            Dim pd = TryCast(prev.Operand, Mono.Cecil.ParameterDefinition)
            'If pd IsNot Nothing Then argIdx = pd.Index
        End If

        If argIdx <> -1 Then
            ' Search backwards for the last starg to this index
            For j As Integer = currentIndex - 2 To 0 Step -1
                Dim scan = instrs(j)
                Dim stIdx = -1
                If scan.OpCode.Code = Code.Starg Or scan.OpCode.Code = Code.Starg_S Then
                    Dim pd2 = TryCast(scan.Operand, Mono.Cecil.ParameterDefinition)
                    If pd2 IsNot Nothing Then stIdx = pd2.Index
                End If

                If stIdx = argIdx Then
                    Return BacktrackCondition(mdef, j)
                End If
            Next
        End If
        'End If

        ' If it's a comparison, get its operands
        If prev.OpCode.Code = Code.Ceq Or prev.OpCode.Code = Code.Cgt Or prev.OpCode.Code = Code.Cgt_Un Or prev.OpCode.Code = Code.Clt Or prev.OpCode.Code = Code.Clt_Un Then
            Return "stack"
        End If

        Return GetInstructionDescription(mdef, prev)
    End Function

    Private Function GetParamTypes(methodRef As Mono.Cecil.MethodReference) As List(Of ObjectIR.Core.IR.TypeReference)
        Select Case methodRef.Parameters.Count
            Case 0
                Return New List(Of ObjectIR.Core.IR.TypeReference)()
            Case Else
                Dim paramTypes As New List(Of ObjectIR.Core.IR.TypeReference)()
                For Each p In methodRef.Parameters
                    Select Case GetTypeName(p.ParameterType)
                        Case "System.Int32"
                            paramTypes.Add(ObjectIR.Core.IR.TypeReference.Int32)
                        Case "System.String"
                            paramTypes.Add(ObjectIR.Core.IR.TypeReference.String)
                        Case Else
                            paramTypes.Add(ObjectIR.Core.IR.TypeReference.Void)
                    End Select
                Next
                Return paramTypes
        End Select
    End Function

    Private Function GetReturnType(methodRef As Mono.Cecil.MethodReference) As ObjectIR.Core.IR.TypeReference
        Dim MethodReturnType = GetTypeName(methodRef.ReturnType)
        Select Case MethodReturnType
            Case "System.Void"
                Return ObjectIR.Core.IR.TypeReference.Void
            Case "System.Int32"
                Return ObjectIR.Core.IR.TypeReference.Int32
            Case "System.String"
                Return ObjectIR.Core.IR.TypeReference.String
            Case Else
                Return ObjectIR.Core.IR.TypeReference.Void
        End Select
    End Function

    Private Function GetTypeReference(instruction As Cil.Instruction) As ObjectIR.Core.IR.TypeReference
        Dim operandType = instruction.Operand.GetType().ToString()
        Select Case operandType
            Case "System.Void"
                Return ObjectIR.Core.IR.TypeReference.Void
            Case "System.Int32"
                Return ObjectIR.Core.IR.TypeReference.Int32
            Case Else
                Return ObjectIR.Core.IR.TypeReference.Void
        End Select

    End Function

    Private Function GetTypeName(t As Mono.Cecil.TypeReference) As String
        If t Is Nothing Then
            Return "void"
        End If

        If TypeOf t Is GenericInstanceType Then
            Dim git = CType(t, GenericInstanceType)
            Dim elName = git.ElementType.FullName
            Dim idx = elName.IndexOf("`"c)
            If idx >= 0 Then
                elName = elName.Substring(0, idx)
            End If
            Dim args As New System.Collections.Generic.List(Of String)
            For Each ga In git.GenericArguments
                args.Add(GetTypeName(ga))
            Next
            Return elName.Replace("/", ".") & "<" & String.Join(", ", args) & ">"
        End If

        If TypeOf t Is ArrayType Then
            Dim at = CType(t, ArrayType)
            Return GetTypeName(at.ElementType) & "[]"
        End If

        If t.IsByReference Then
            Dim br = CType(t, ByReferenceType)
            Return GetTypeName(br.ElementType) & "&"
        End If

        Return t.FullName.Replace("/", ".")
    End Function

    Private Function FormatMethodDefinition(mdef As Mono.Cecil.MethodDefinition) As String
        Dim accessor = GetMethodAccessor(mdef)
        Dim name = mdef.Name

        Dim paramList As New System.Collections.Generic.List(Of String)
        For Each p In mdef.Parameters
            paramList.Add($"{GetTypeName(p.ParameterType)} {p.Name}")
        Next

        Dim paramsStr = String.Join(", ", paramList)
        Dim ret = GetTypeName(mdef.ReturnType)
        Return $"{accessor} {name} ({paramsStr}) -> {ret}"
    End Function

    Private Function FormatMethodSignature(m As Mono.Cecil.MethodReference) As String
        Dim decl = If(m.DeclaringType IsNot Nothing, m.DeclaringType.FullName.Replace("/", "."), "<Module>")
        Dim name = m.Name
        Dim paramList As New System.Collections.Generic.List(Of String)
        For Each p In m.Parameters
            paramList.Add(GetTypeName(p.ParameterType))
        Next
        Dim paramsStr = String.Join(", ", paramList)
        Dim ret = GetTypeName(m.ReturnType)
        Return $"call {decl}.{name}({paramsStr}) -> {ret}"
    End Function

    Private Function GetMethodAccessor(mdef As Mono.Cecil.MethodDefinition) As String
        If mdef Is Nothing Then
            Return "method"
        End If

        Dim parts As New System.Collections.Generic.List(Of String)
        If mdef.IsPublic Then
            parts.Add("public")
        ElseIf mdef.IsPrivate Then
            parts.Add("private")
        ElseIf mdef.IsFamily Then
            parts.Add("protected")
        ElseIf mdef.IsAssembly Then
            parts.Add("internal")
        ElseIf mdef.IsFamilyOrAssembly Then
            parts.Add("protected internal")
        ElseIf mdef.IsFamilyAndAssembly Then
            parts.Add("private protected")
        End If

        If mdef.IsStatic Then
            parts.Add("static")
        End If

        If parts.Count = 0 Then
            Return "method"
        End If

        Return String.Join(" ", parts) & " method"
    End Function

    Private Function FormatMethodWithAccess(mref As Mono.Cecil.MethodReference) As String
        Dim mdef As Mono.Cecil.MethodDefinition = Nothing
        Try
            mdef = mref.Resolve()
        Catch
            mdef = Nothing
        End Try

        Dim accessor = GetMethodAccessor(mdef)
        Dim name = mref.Name

        Dim paramList As New System.Collections.Generic.List(Of String)
        If mdef IsNot Nothing Then
            For Each p In mdef.Parameters
                paramList.Add($"{GetTypeName(p.ParameterType)} {p.Name}")
            Next
        Else
            For Each p In mref.Parameters
                paramList.Add(GetTypeName(p.ParameterType))
            Next
        End If

        Dim paramsStr = String.Join(", ", paramList)
        Dim ret = GetTypeName(mref.ReturnType)
        Return $"{accessor} {name} ({paramsStr}) -> {ret}"
    End Function
End Module
