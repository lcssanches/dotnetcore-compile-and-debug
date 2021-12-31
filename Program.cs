
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;


while (true)
{
    var TABELA_FUNCTIONS = JsonConvert.DeserializeObject<IList<FnRecord>>(File.ReadAllText("functions.json"));

    Console.WriteLine("Funções disponíveis: ");
    foreach (var item in TABELA_FUNCTIONS!)
    {
        Console.WriteLine(item.Name);
    }

    Console.WriteLine("Digite o nome da função para executar: ");
    var functionNameToExecute = Console.ReadLine();

    TABELA_FUNCTIONS = JsonConvert.DeserializeObject<IList<FnRecord>>(File.ReadAllText("functions.json"));

    var function = TABELA_FUNCTIONS.SingleOrDefault(x => x.Name == functionNameToExecute);

    if (function == null)
    {
        Console.WriteLine("funcao nao encontrada");
        continue;
    }


    Directory.CreateDirectory("generated");

    var fnAssemblyPath = $"generated/{function.Name}.dll";
    var fnAssemblyPdbPath = $"generated/{function.Name}.pdb";
    var sourceFilePath = $"generated/{function.Name}.cs";



    if (!File.Exists(fnAssemblyPath))
    {
        File.WriteAllLines(sourceFilePath, function.Source!);
        Console.WriteLine("Criando nova versao...");
        CreateAssembly(fnAssemblyPath, sourceFilePath);
    }

    var classFullQualifiedName = $"CustomCode.{functionNameToExecute}";

    

    Type functionClassType = GetAssembly(fnAssemblyPath,fnAssemblyPdbPath)
            .GetType(classFullQualifiedName)
                    ?? throw new ArgumentException("Classe nao exite:" + classFullQualifiedName);

    var field = functionClassType
        .GetField("VERSION", BindingFlags.Public | BindingFlags.Static)
            ?? throw new ArgumentException("Membro estatico VERSION nao existe");

    var cachedVersion = (string)(field.GetValue(null) ?? "");

    if (cachedVersion != function.Version)
    {
        foreach (var file in Directory.EnumerateFiles("./", $"generated/{function.Name}.*")) {
            File.Delete(file);
        }

        File.WriteAllLines(sourceFilePath, function.Source!);

        Console.WriteLine("Versao do banco diferente da versao compilada. Gerando nova versao.");
        CreateAssembly(fnAssemblyPath, sourceFilePath);
        functionClassType = GetAssembly(fnAssemblyPath,fnAssemblyPdbPath)
            .GetType(classFullQualifiedName)
        ?? throw new ArgumentException(classFullQualifiedName);
    }

    var executeMethod = functionClassType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? throw new ArgumentException("Método Run nao existe");
    string result = (string)(executeMethod.Invoke(null, new object[] { DateTime.Now.ToString() }) ?? "");

    Console.WriteLine("Resultado: " + result);
}

Assembly GetAssembly(string fnAssemblyPath, string fnAssemblyPdbPath) {
    
    // using var assemblyStream = new FileStream(fnAssemblyPath, FileMode.Open, FileAccess.Read);
    // using var pdbStream = new FileStream(fnAssemblyPdbPath, FileMode.Open, FileAccess.Read);
    



    return Assembly.Load(File.ReadAllBytes(fnAssemblyPath), File.ReadAllBytes(fnAssemblyPdbPath));
    // return AssemblyLoadContext.Default.LoadFromStream(
    //     assemblyStream, 
    //     pdbStream 
    // );

}

void CreateAssembly(string outputFilePath, string sourceCodeFile)
{


    var assemblyName = Path.GetFileName(outputFilePath);
    var code = File.ReadAllText(sourceCodeFile);
    var dotNetCoreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly!.Location!)!;

    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
        code,
        encoding: System.Text.Encoding.UTF8,
        path: sourceCodeFile);
    CSharpCompilation compilation = CSharpCompilation.Create(
        assemblyName,
        new[] { syntaxTree },
        new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DynamicAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir, "System.Linq.dll"))
        },
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithPlatform(Platform.AnyCpu)
    );

    using var peStream = new MemoryStream();
    using var pdbStream = new MemoryStream();

    var emitOptions = new EmitOptions(
        debugInformationFormat: DebugInformationFormat.PortablePdb,

        pdbFilePath: Path.ChangeExtension(assemblyName, "pdb")
    );

    var encoding = System.Text.Encoding.UTF8;

    var buffer = encoding.GetBytes(code);
    var sourceText = SourceText.From(buffer, buffer.Length, encoding, canBeEmbedded: true);

    var embeddedTexts = new List<EmbeddedText>
        {
            EmbeddedText.FromSource(sourceCodeFile, sourceText),
        };

    EmitResult result = compilation.Emit(
        peStream: peStream,
        pdbStream: pdbStream,
            embeddedTexts: embeddedTexts,
        options: emitOptions
    );

    if (result.Success)
    {
        peStream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(outputFilePath, peStream.ToArray());
        pdbStream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(Path.ChangeExtension(outputFilePath, "pdb"), pdbStream.ToArray());
        return;
    }

    foreach (var item in result.Diagnostics)
    {
        Console.WriteLine(item.ToString());
    }

    Console.WriteLine("Erro na compilação. Cheque os erros acima.");


}