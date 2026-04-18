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
        Dim CurrentAssembly = Assembly.GetAssembly(GetType(TestingApp.Program)).Location
        Dim ASM = AssemblyDefinition.ReadAssembly(CurrentAssembly)
        Dim IRbuilder As New IRBuilder(ASM.Name.Name.ToString())
        For Each Type In ASM.MainModule.Types
            If Type.Name = "<Module>" Then Continue For
            ProcessType(Type, IRbuilder)
        Next

        Console.WriteLine(IRbuilder.Build().Serialize().DumpToIRCode())
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
            Dim fldType As ObjectIR.Core.IR.TypeReference
            Select Case fldTypeName
                Case "System.Int32"
                    fldType = ObjectIR.Core.IR.TypeReference.Int32
                Case "System.String"
                    fldType = ObjectIR.Core.IR.TypeReference.String
                Case Else
                    fldType = ObjectIR.Core.IR.TypeReference.Void
            End Select
            Dim fb = classBuilder.Field(FieldDef.Name, fldType)
            If FieldDef.IsStatic Then fb.Static()
            fb.EndField()
        Next
    End Sub

    Private Sub ProcessMethod(mdef As Mono.Cecil.MethodDefinition, classBuilder As ObjectIR.Core.Builder.ClassBuilder)
        Dim MethodBuilder As ObjectIR.Core.Builder.MethodBuilder
        Select Case GetTypeName(mdef.ReturnType)
            Case "System.Void"
                MethodBuilder = classBuilder.Method(mdef.Name, ObjectIR.Core.IR.TypeReference.Void)
            Case "System.Int32"
                MethodBuilder = classBuilder.Method(mdef.Name, ObjectIR.Core.IR.TypeReference.Int32)
            Case "System.String"
                MethodBuilder = classBuilder.Method(mdef.Name, ObjectIR.Core.IR.TypeReference.String)
            Case Else
                MethodBuilder = classBuilder.Method(mdef.Name, ObjectIR.Core.IR.TypeReference.Void)
        End Select

        If mdef.IsStatic Then MethodBuilder.Static()
        If mdef.IsAbstract Then MethodBuilder.Abstract()

        AddMethodParameters(mdef, MethodBuilder)
        AddMethodLocals(mdef, MethodBuilder)
        BuildInstructions(mdef, MethodBuilder)
    End Sub

    Private Sub AddMethodParameters(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        For Each p In mdef.Parameters
            Dim pTypeName = GetTypeName(p.ParameterType)
            Select Case pTypeName
                Case "System.Int32"
                    mb.Parameter(p.Name, ObjectIR.Core.IR.TypeReference.Int32)
                Case "System.String"
                    mb.Parameter(p.Name, ObjectIR.Core.IR.TypeReference.String)
                Case Else
                    mb.Parameter(p.Name, ObjectIR.Core.IR.TypeReference.Void)
            End Select
        Next
    End Sub

    Private Sub AddMethodLocals(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        If mdef.HasBody AndAlso mdef.Body IsNot Nothing Then
            For i As Integer = 0 To mdef.Body.Variables.Count - 1
                Dim FieldReferences = mdef.Body.Variables(i)
                Dim FieldType = GetTypeName(FieldReferences.VariableType)
                Dim localName = If(String.IsNullOrEmpty(FieldReferences.Index.ToString()), "loc" & i.ToString(), FieldReferences.Index.ToString())
                Select Case FieldType
                    Case "System.Int32"
                        mb.Local(localName, ObjectIR.Core.IR.TypeReference.Int32)
                    Case "System.String"
                        mb.Local(localName, ObjectIR.Core.IR.TypeReference.String)
                    Case Else
                        mb.Local(localName, ObjectIR.Core.IR.TypeReference.Void)
                End Select
            Next
        End If
    End Sub

    Private Sub BuildInstructions(mdef As Mono.Cecil.MethodDefinition, mb As ObjectIR.Core.Builder.MethodBuilder)
        Dim Instructions = mb.Body()
        If Not (mdef.HasBody AndAlso mdef.Body IsNot Nothing) Then
            mb.EndMethod()
            Return
        End If
        ' Parse method body normally. Don't treat op_Equality specially here;
        ' let the instruction-level parser map calls and branches into control flow.
        ParseInstructions(mdef.Body.Instructions, mb.Body())
        mb.EndMethod()
    End Sub
    Private Sub ParseInstructions(instr As Mono.Collections.Generic.Collection(Of Mono.Cecil.Cil.Instruction), mb As ObjectIR.Core.Builder.InstructionBuilder, Optional startIndex As Integer = 0, Optional endIndex As Integer = -1)
        Dim instructions = mb
        If endIndex = -1 Then endIndex = instr.Count
        Dim i As Integer = startIndex
        While i < endIndex
            Dim Instructionz = instr(i)
            Select Case Instructionz.OpCode.Code
                Case Mono.Cecil.Cil.Code.Call, Mono.Cecil.Cil.Code.Callvirt
                    Dim MethodRef = TryCast(Instructionz.Operand, Mono.Cecil.MethodReference)
                    If MethodRef IsNot Nothing Then
                        Dim declTypeRef = ObjectIR.Core.IR.TypeReference.FromName(GetTypeName(MethodRef.DeclaringType))
                        ' Convert equality operator calls to a compare-equal instruction
                        If MethodRef.Name = "op_Equality" AndAlso GetReturnType(MethodRef).Equals(ObjectIR.Core.IR.TypeReference.Int32) = False Then
                            ' op_Equality returns bool -> emit compare-equal instead of call
                            instructions.Ceq()
                        Else
                            If Instructionz.OpCode.Code = Mono.Cecil.Cil.Code.Callvirt Then
                                instructions.Callvirt(New ObjectIR.Core.IR.MethodReference(declTypeRef, MethodRef.Name, GetReturnType(MethodRef), GetParamTypes(MethodRef)))
                            Else
                                instructions.Call(New ObjectIR.Core.IR.MethodReference(declTypeRef, MethodRef.Name, GetReturnType(MethodRef), GetParamTypes(MethodRef)))
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
                        Dim ty = ObjectIR.Core.IR.TypeReference.FromName(GetTypeName(ctorRef.DeclaringType))
                        instructions.Newobj(ty)
                    End If
                Case Code.Newarr
                    Dim arrType = TryCast(Instructionz.Operand, Mono.Cecil.TypeReference)
                    If arrType IsNot Nothing Then
                        instructions.Newarr(ObjectIR.Core.IR.TypeReference.FromName(GetTypeName(arrType)))
                    End If
                Case Code.Ldnull
                    instructions.Ldnull()
                Case Code.Ceq
                    instructions.Ceq()
                Case Code.Ldarg, Code.Ldarg_S, Code.Ldarg_0, Code.Ldarg_1, Code.Ldarg_2, Code.Ldarg_3
                    Dim idxArg As Integer = -1
                    If Instructionz.OpCode.Code = Code.Ldarg_0 Then idxArg = 0
                    If Instructionz.OpCode.Code = Code.Ldarg_1 Then idxArg = 1
                    If Instructionz.OpCode.Code = Code.Ldarg_2 Then idxArg = 2
                    If Instructionz.OpCode.Code = Code.Ldarg_3 Then idxArg = 3
                    If idxArg = -1 AndAlso Instructionz.Operand IsNot Nothing Then
                        Dim pd = TryCast(Instructionz.Operand, Mono.Cecil.ParameterDefinition)
                        If pd IsNot Nothing Then idxArg = pd.Index
                    End If
                    If idxArg >= 0 Then instructions.Ldarg(idxArg)

                Case Code.Ldstr
                    Dim Str = TryCast(Instructionz.Operand, String)
                    If Str IsNot Nothing Then
                        instructions.Ldstr(Str)
                    End If
                Case Code.Castclass
                    ' TODO: map castclass
                Case Code.Ldc_I4, Code.Ldc_I4_S, Code.Ldc_I4_0, Code.Ldc_I4_1, Code.Ldc_I4_2, Code.Ldc_I4_3, Code.Ldc_I4_4, Code.Ldc_I4_5, Code.Ldc_I4_6, Code.Ldc_I4_7, Code.Ldc_I4_8
                    Dim value As Integer
                    If Instructionz.OpCode.Code = Code.Ldc_I4_S Then
                        value = CInt(Instructionz.Operand)
                    ElseIf Instructionz.OpCode.Code >= Code.Ldc_I4_0 AndAlso Instructionz.OpCode.Code <= Code.Ldc_I4_8 Then
                        value = Instructionz.OpCode.Code - Code.Ldc_I4_0
                    Else
                        value = CInt(Instructionz.Operand)
                    End If
                    instructions.LdcI4(value)
                Case Code.Add
                    instructions.Add()
                Case Code.Sub
                    instructions.Sub()
                Case Code.Mul
                    instructions.Mul()
                Case Code.Div
                    instructions.Div()
                Case Code.Ldfld, Code.Ldsfld
                    Dim FieldRef = TryCast(Instructionz.Operand, Mono.Cecil.FieldReference)
                    If FieldRef IsNot Nothing Then
                        Dim fr = New ObjectIR.Core.IR.FieldReference(ObjectIR.Core.IR.TypeReference.FromName(GetTypeName(FieldRef.DeclaringType)), FieldRef.Name, ObjectIR.Core.IR.TypeReference.FromName(GetTypeName(FieldRef.FieldType)))
                        If Instructionz.OpCode.Code = Code.Ldfld Then
                            instructions.Ldfld(fr)
                        Else
                            instructions.Ldsfld(fr)
                        End If
                    End If
                Case Code.Stloc, Code.Stloc_S, Code.Stloc_0, Code.Stloc_1, Code.Stloc_2, Code.Stloc_3
                    Dim localName As String = Nothing
                    If Instructionz.OpCode.Code = Code.Stloc_0 Then
                        localName = "0"
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_1 Then
                        localName = "1"
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_2 Then
                        localName = "2"
                    ElseIf Instructionz.OpCode.Code = Code.Stloc_3 Then
                        localName = "3"
                        'ElseIf Instructionz.Operand IsNot Nothing Then
                        '    Dim v = TryCast(Instructionz.Operand, Mono.Cecil.)
                        '    If v IsNot Nothing Then localName = v.Index.ToString()
                    End If
                    If Not String.IsNullOrEmpty(localName) Then
                        instructions.Stloc(localName)
                    End If
                Case Code.Ldloc, Code.Ldloc_S, Code.Ldloc_0, Code.Ldloc_1, Code.Ldloc_2, Code.Ldloc_3
                    Dim localName As String = Nothing
                    If Instructionz.OpCode.Code = Code.Ldloc_0 Then
                        localName = "0"
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_1 Then
                        localName = "1"
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_2 Then
                        localName = "2"
                    ElseIf Instructionz.OpCode.Code = Code.Ldloc_3 Then
                        localName = "3"
                        'ElseIf Instructionz.Operand IsNot Nothing Then
                        '    Dim v = TryCast(Instructionz.Operand, Mono.Cecil.VariableDefinition)
                        '    If v IsNot Nothing Then localName = v.Index.ToString()
                    End If
                    If Not String.IsNullOrEmpty(localName) Then
                        instructions.Ldloc(localName)
                    End If
                ' Case Code.Ldelem
                '     instructions.Ldelem()
                ' Case Code.Stelem
                '     instructions.Stelem()
                ' Case Code.Ldlen
                '     instructions.Ldlen()
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
                                instructions.If(Condition.Stack(), Sub(thenBuilder)
                                                                       ParseInstructions(instr, thenBuilder, i + 1, thenEndIndex - 1)
                                                                   End Sub,
                                                                   Sub(elseBuilder)
                                                                       ParseInstructions(instr, elseBuilder, thenEndIndex, endIndexz)
                                                                   End Sub)
                                ' Skip to endIndex
                                i = endIndexz - 1
                            Else
                                ' No else: simple if then-only
                                instructions.If(Condition.Stack(), Sub(thenBuilder)
                                                                       ParseInstructions(instr, thenBuilder, i + 1, targetIndex)
                                                                   End Sub)
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
                                instructions.If(Condition.Stack(), Sub(thenBuilder)
                                                                       ParseInstructions(instr, thenBuilder, targetIndexT, endIndexT)
                                                                   End Sub,
                                                                   Sub(elseBuilder)
                                                                       ParseInstructions(instr, elseBuilder, elseStart, elseEndCandidate - 1)
                                                                   End Sub)
                                i = endIndexT - 1
                            Else
                                ' Fallback: treat as simple if with then at target (no else)
                                ' We'll emit the condition and then parse then-block
                                instructions.If(Condition.Stack(), Sub(thenBuilder)
                                                                       ParseInstructions(instr, thenBuilder, targetIndexT, instr.Count)
                                                                   End Sub)
                                i = targetIndexT - 1
                            End If
                        End If
                    End If
            End Select
            i += 1
        End While

    End Sub
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
