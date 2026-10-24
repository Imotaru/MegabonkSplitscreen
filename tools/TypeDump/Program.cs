// Dumps classes, fields, properties and methods of a .NET assembly to a text file, using only the
// in-box System.Reflection.Metadata reader (no loading, nothing executed).
// Usage: TypeDump <input.dll> <output.txt>
// Il2CppInterop plumbing members (NativeFieldInfoPtr_*, NativeMethodInfoPtr_*, ...) are skipped.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

if (args.Length != 2)
{
    Console.WriteLine("Usage: TypeDump <input.dll> <output.txt>");
    return 1;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var types = new TypeNameProvider(md);
var sb = new StringBuilder();
int typeCount = 0;

foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    string name = TypeNameProvider.FullName(md, handle);
    if (name == "<Module>" || name.Contains("<")) continue;
    typeCount++;

    string kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface" : "class";
    string baseType = type.BaseType.IsNil ? "" : " : " + types.NameOf(type.BaseType);
    sb.AppendLine($"{kind} {name}{baseType}");

    foreach (var fh in type.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        string fname = md.GetString(f.Name);
        if (IsPlumbing(fname)) continue;
        string stat = (f.Attributes & FieldAttributes.Static) != 0 ? "static " : "";
        sb.AppendLine($"    field {stat}{f.DecodeSignature(types, null)} {fname}");
    }

    foreach (var ph in type.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        var sig = p.DecodeSignature(types, null);
        string stat = sig.Header.IsInstance ? "" : "static ";
        sb.AppendLine($"    prop {stat}{sig.ReturnType} {md.GetString(p.Name)}");
    }

    foreach (var mh in type.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string mname = md.GetString(m.Name);
        if (IsPlumbing(mname) || mname.StartsWith("get_") || mname.StartsWith("set_")) continue;
        var sig = m.DecodeSignature(types, null);
        var paramNames = m.GetParameters().Select(p => md.GetParameter(p))
            .Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber)
            .Select(p => md.GetString(p.Name)).ToList();
        var ps = sig.ParameterTypes.Select((t, i) => i < paramNames.Count ? $"{t} {paramNames[i]}" : t);
        string stat = (m.Attributes & MethodAttributes.Static) != 0 ? "static " : "";
        sb.AppendLine($"    method {stat}{sig.ReturnType} {mname}({string.Join(", ", ps)})");
    }
}

File.WriteAllText(args[1], sb.ToString());
Console.WriteLine($"Wrote {typeCount} types to {args[1]}");
return 0;

static bool IsPlumbing(string n) =>
    n.StartsWith("NativeFieldInfoPtr_") || n.StartsWith("NativeMethodInfoPtr_") ||
    n.StartsWith("NativePropertyInfoPtr_") || n == ".cctor" || n.Contains("<");

// Turns metadata type signatures into readable names like "List<Enemy>".
class TypeNameProvider : ISignatureTypeProvider<string, object>
{
    private readonly MetadataReader md;
    public TypeNameProvider(MetadataReader md) { this.md = md; }

    public static string FullName(MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        string name = md.GetString(t.Name);
        var declaring = t.GetDeclaringType();
        if (!declaring.IsNil) return FullName(md, declaring) + "+" + name;
        string ns = md.GetString(t.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    public string NameOf(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)h, 0),
        HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(md, null, (TypeSpecificationHandle)h, 0),
        _ => "?"
    };

    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => md.GetString(md.GetTypeDefinition(h).Name);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => md.GetString(md.GetTypeReference(h).Name);
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => md.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString().ToLowerInvariant();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetPinnedType(string e) => e;
    public string GetGenericInstantiation(string g, ImmutableArray<string> a)
    {
        int tick = g.IndexOf('`');
        return (tick >= 0 ? g[..tick] : g) + "<" + string.Join(", ", a) + ">";
    }
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string m, string u, bool req) => u;
}
