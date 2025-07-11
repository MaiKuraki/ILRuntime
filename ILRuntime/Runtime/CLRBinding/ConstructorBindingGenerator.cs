﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using ILRuntime.Runtime.Enviorment;

namespace ILRuntime.Runtime.CLRBinding
{
    static class ConstructorBindingGenerator
    {
        internal static string GenerateConstructorRegisterCode(this Type type, ConstructorInfo[] methods, HashSet<MethodBase> excludes)
        {
            StringBuilder sb = new StringBuilder();
            int idx = 0;
            foreach (var i in methods)
            {
                if (excludes != null && excludes.Contains(i))
                    continue;
                if (type.ShouldSkipMethod(i))
                    continue;
                var param = i.GetParameters();
                StringBuilder sb2 = new StringBuilder();
                sb2.Append("{");
                bool first = true;
                foreach (var j in param)
                {
                    if (first)
                        first = false;
                    else
                        sb2.Append(", ");
                    sb2.Append("typeof(");
                    string clsName, realClsName;
                    bool isByRef;
                    j.ParameterType.GetClassName(out clsName, out realClsName, out isByRef);
                    sb2.Append(realClsName);
                    sb2.Append(")");
                    if (isByRef)
                        sb2.Append(".MakeByRefType()");
                }
                sb2.Append("}");
                sb.AppendLine(string.Format("            args = new Type[]{0};", sb2));
                sb.AppendLine("            method = type.GetConstructor(flag, null, args, null);");
                sb.AppendLine(string.Format("            app.RegisterCLRMethodRedirection(method, Ctor_{0});",idx));

                idx++;
            }
            return sb.ToString();
        }

        internal static string GenerateConstructorWraperCode(this Type type, ConstructorInfo[] methods, string typeClsName, HashSet<MethodBase> excludes, List<Type> valueTypeBinders)
        {
            StringBuilder sb = new StringBuilder();

            int idx = 0;
            bool isMultiArr = type.IsArray && type.GetArrayRank() > 1;
            foreach (var i in methods)
            {
                if (excludes != null && excludes.Contains(i))
                    continue;
                if (type.ShouldSkipMethod(i) || i.IsStatic)
                    continue;
                var param = i.GetParameters();
                int paramCnt = param.Length;
                sb.AppendLine(string.Format("        static StackObject* Ctor_{0}(ILIntepreter __intp, StackObject* __esp, AutoList __mStack, CLRMethod __method, bool isNewObj)", idx));
                sb.AppendLine("        {");
                sb.AppendLine("            ILRuntime.Runtime.Enviorment.AppDomain __domain = __intp.AppDomain;");
                if (param.Length != 0)
                    sb.AppendLine("            StackObject* ptr_of_this_method;");
                sb.AppendLine(string.Format("            StackObject* __ret = __esp - {0};", paramCnt));
                bool hasByRef = param.HasByRefParam();
                string shouldFreeParam = hasByRef ? "false" : "true";
                for (int j = param.Length; j > 0; j--)
                {
                    var p = param[j - 1];
                    sb.AppendLine(string.Format("            ptr_of_this_method = __esp - {0};", param.Length - j + 1));
                    string clsName, realClsName;
                    bool isByRef;
                    p.ParameterType.GetClassName(out clsName, out realClsName, out isByRef);

                    var pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    if (pt.IsValueType && !pt.IsPrimitive && valueTypeBinders != null && valueTypeBinders.Contains(pt))
                    {
                        if (isMultiArr)
                            sb.AppendLine(string.Format("            {0} a{1} = new {0}();", realClsName, j));
                        else
                            sb.AppendLine(string.Format("            {0} @{1} = new {0}();", realClsName, p.Name));

                        sb.AppendLine(string.Format("            if (ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder != null) {{", clsName));

                        if (isMultiArr)
                            sb.AppendLine(string.Format("                ILRuntime.Runtime.Generated.CLRBindings.s_{1}_Binder.ParseValue(ref a{0}, __intp, ptr_of_this_method, __mStack, {2});", j, clsName, shouldFreeParam));
                        else
                            sb.AppendLine(string.Format("                ILRuntime.Runtime.Generated.CLRBindings.s_{1}_Binder.ParseValue(ref @{0}, __intp, ptr_of_this_method, __mStack, {2});", p.Name, clsName, shouldFreeParam));

                        sb.AppendLine("            } else {");

                        if (isByRef)
                            sb.AppendLine("                ptr_of_this_method = ILIntepreter.GetObjectAndResolveReference(ptr_of_this_method);");
                        if (isMultiArr)
                            sb.AppendLine(string.Format("                a{0} = {1};", j, p.ParameterType.GetRetrieveValueCode(realClsName)));
                        else
                            sb.AppendLine(string.Format("                @{0} = {1};", p.Name, p.ParameterType.GetRetrieveValueCode(realClsName)));
                        if (!hasByRef)
                            sb.AppendLine("                __intp.Free(ptr_of_this_method);");

                        sb.AppendLine("            }");
                    }
                    else
                    {
                        if (isByRef)
                        {
                            if (p.ParameterType.GetElementType().IsPrimitive)
                            {
                                if (pt == typeof(int) || pt == typeof(uint) || pt == typeof(short) || pt == typeof(ushort) || pt == typeof(byte) || pt == typeof(sbyte) || pt == typeof(char))
                                {
                                    if (pt == typeof(int))
                                        sb.AppendLine(string.Format("            {0} @{1} = __intp.RetriveInt32(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                    else
                                        sb.AppendLine(string.Format("            {0} @{1} = ({0})__intp.RetriveInt32(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                }
                                else if (pt == typeof(long) || pt == typeof(ulong))
                                {
                                    if (pt == typeof(long))
                                        sb.AppendLine(string.Format("            {0} @{1} = __intp.RetriveInt64(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                    else
                                        sb.AppendLine(string.Format("            {0} @{1} = ({0})__intp.RetriveInt64(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                }
                                else if (pt == typeof(float))
                                {
                                    sb.AppendLine(string.Format("            {0} @{1} = __intp.RetriveFloat(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                }
                                else if (pt == typeof(double))
                                {
                                    sb.AppendLine(string.Format("            {0} @{1} = __intp.RetriveDouble(ptr_of_this_method, __mStack);", realClsName, p.Name));
                                }
                                else if (pt == typeof(bool))
                                {
                                    sb.AppendLine(string.Format("            {0} @{1} = __intp.RetriveInt32(ptr_of_this_method, __mStack) == 1;", realClsName, p.Name));
                                }
                                else
                                    throw new NotSupportedException();
                            }
                            else
                            {
                                sb.AppendLine(string.Format("            {0} @{1} = ({0})typeof({0}).CheckCLRTypes(__intp.RetriveObject(ptr_of_this_method, __mStack));", realClsName, p.Name));
                            }

                        }
                        else
                        {
                            if (isMultiArr)
                                sb.AppendLine(string.Format("            {0} a{1} = {2};", realClsName, j, p.ParameterType.GetRetrieveValueCode(realClsName)));
                            else
                                sb.AppendLine(string.Format("            {0} @{1} = {2};", realClsName, p.Name, p.ParameterType.GetRetrieveValueCode(realClsName)));
                            if (!hasByRef && !p.ParameterType.IsPrimitive)
                                sb.AppendLine("            __intp.Free(ptr_of_this_method);");

                        }
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
                sb.Append("            var result_of_this_method = ");
                {
                    string clsName, realClsName;
                    bool isByRef;
                    if (isMultiArr)
                    {
                        type.GetElementType().GetClassName(out clsName, out realClsName, out isByRef);
                        sb.Append(string.Format("new {0}[", realClsName));
                        param.AppendParameters(sb, isMultiArr);
                        sb.AppendLine("];");
                    }
                    else
                    {
                        type.GetClassName(out clsName, out realClsName, out isByRef);
                        sb.Append(string.Format("new {0}(", realClsName));
                        param.AppendParameters(sb, isMultiArr);
                        sb.AppendLine(");");
                    }
                }
                sb.AppendLine();

                //Ref/Out
                for (int j = param.Length; j > 0; j--)
                {
                    var p = param[j - 1];
                    if (!p.ParameterType.IsByRef && !hasByRef)
                    {
                        continue;
                    }
                    string clsName, realClsName;
                    bool isByRef;
                    var pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    pt.GetClassName(out clsName, out realClsName, out isByRef);
                    sb.AppendLine(string.Format("            ptr_of_this_method = __esp - {0};", param.Length - j + 1));
                    if (p.ParameterType.IsByRef)
                    {
                        sb.AppendLine(@"            switch(ptr_of_this_method->ObjectType)
            {
                case ObjectTypes.StackObjectReference:
                    {
                        var ___dst = ILIntepreter.ResolveReference(ptr_of_this_method);");

                        if (p.ParameterType.IsValueType && !p.ParameterType.IsPrimitive && valueTypeBinders != null && valueTypeBinders.Contains(p.ParameterType))
                        {
                            sb.AppendLine(string.Format("                if (ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder != null) {{", clsName));

                            sb.AppendLine(string.Format("                        ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder.WriteBackValue(__domain, ptr_of_this_method, __mStack, ref {1});", realClsName, p.Name));

                            sb.AppendLine("                } else {");

                            p.ParameterType.GetElementType().GetRefWriteBackValueCode(sb, p.Name);

                            sb.AppendLine("                }");
                        }
                        else
                        {
                            p.ParameterType.GetElementType().GetRefWriteBackValueCode(sb, p.Name);
                        }

                        sb.Append(@"                    }
                    break;
                case ObjectTypes.FieldReference:
                    {
                        var ___obj = __mStack[ptr_of_this_method->Value];
                        if(___obj is ILTypeInstance)
                        {
                            ((ILTypeInstance)___obj)[ptr_of_this_method->ValueLow] = ");
                        sb.Append(p.Name);
                        sb.Append(@";
                        }
                        else
                        {
                            var t = __domain.GetType(___obj.GetType()) as CLRType;
                            t.SetFieldValue(ptr_of_this_method->ValueLow, ref ___obj, ");
                        sb.Append(p.Name);
                        sb.Append(@");
                        }
                    }
                    break;
                case ObjectTypes.StaticFieldReference:
                    {
                        var t = __domain.GetType(ptr_of_this_method->Value);
                        if(t is ILType)
                        {
                            ((ILType)t).StaticInstance[ptr_of_this_method->ValueLow] = ");
                        sb.Append(p.Name);
                        sb.Append(@";
                        }
                        else
                        {
                            ((CLRType)t).SetStaticFieldValue(ptr_of_this_method->ValueLow, ");
                        sb.Append(p.Name);
                        sb.Append(@");
                        }
                    }
                    break;
                 case ObjectTypes.ArrayReference:
                    {
                        var instance_of_arrayReference = __mStack[ptr_of_this_method->Value] as ");
                        sb.Append(realClsName);
                        sb.Append(@"[];
                        instance_of_arrayReference[ptr_of_this_method->ValueLow] = ");
                        sb.Append(p.Name);
                        sb.AppendLine(@";
                    }
                    break;
            }");
                        sb.AppendLine();
                    }
                    else if (pt.IsValueType && !pt.IsPrimitive)
                    {
                        sb.AppendLine("            __intp.FreeStackValueType(ptr_of_this_method);");
                    }

                    sb.AppendLine("            __intp.Free(ptr_of_this_method);");
                }

                if (type.IsValueType)
                {
                    if (valueTypeBinders != null && valueTypeBinders.Contains(type))
                    {
                        string clsName, realClsName;
                        bool isByRef;
                        type.GetClassName(out clsName, out realClsName, out isByRef);

                        sb.AppendLine(@"            if(!isNewObj)
            {
                __ret--;");

                        sb.AppendLine(string.Format("                if (ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder != null) {{", clsName));

                        sb.AppendLine(string.Format("                    ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder.WriteBackValue(__domain, __ret, __mStack, ref result_of_this_method);", clsName));

                        sb.AppendLine("                } else {");

                        sb.AppendLine("                    WriteBackInstance(__domain, __ret, __mStack, ref result_of_this_method);");

                        sb.AppendLine("                }");

                        sb.AppendLine(@"                return __ret;
            }");
                    }
                    else
                    {
                        sb.AppendLine(@"            if(!isNewObj)
            {
                __ret--;
                WriteBackInstance(__domain, __ret, __mStack, ref result_of_this_method);
                return __ret;
            }");
                    }
                    sb.AppendLine();
                }

                if (type.IsValueType && valueTypeBinders != null && valueTypeBinders.Contains(type))
                {
                    string clsName, realClsName;
                    bool isByRef;
                    type.GetClassName(out clsName, out realClsName, out isByRef);

                    sb.AppendLine(string.Format("            if (ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder != null) {{", clsName));

                    sb.AppendLine(string.Format("                ILRuntime.Runtime.Generated.CLRBindings.s_{0}_Binder.PushValue(ref result_of_this_method, __intp, __ret, __mStack);", clsName));
                    sb.AppendLine("                return __ret + 1;");

                    sb.AppendLine("            } else {");

                    sb.AppendLine("                return ILIntepreter.PushObject(__ret, __mStack, result_of_this_method);");

                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine("            return ILIntepreter.PushObject(__ret, __mStack, result_of_this_method);");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
                idx++;
            }

            return sb.ToString();
        }
    }
}
