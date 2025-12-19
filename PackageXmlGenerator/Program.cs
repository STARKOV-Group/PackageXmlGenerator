using CommandLine;
using PackageXmlGenerator.Properties;
using System.Text.Json.Nodes;
using System.Xml;

internal class Program
{


    public class LaunchOptions
    {
        [Option('p', "projectPath", Required = true, HelpText = "Папка со всеми репозиториями. Такой обычно называют git_repository")]
        public string ProjectPath { get; set; }

        [Option('o', "outPath", Required = false, HelpText = "Путь выгрузки готового пакета. В конце добавляется имя \\packageGen.xml")]
        public string OutPath { get; set; }

        [Option('l', "solutionList", Required = false, Default = "", HelpText = "Список решений для сборки в пакет. Перечисляются через разделитель \"|\"")]
        public string SolutionList { get; set; }

        [Option('a', "includeAssemblies", Required = false, Default = false, HelpText = "Включить в пакет исполняемые файлы")]
        public bool IncludeAssemblies { get; set; }

        [Option('d', "isDebugPackage", Required = false, Default = false, HelpText = "Передать как отладочный пакет (не работает без признака includeAssemblies)")]
        public bool IsDebugPackage { get; set; }

        [Option('s', "includeSources", Required = false, Default = false, HelpText = "Включить в пакет исходные коды")]
        public bool IncludeSources { get; set; }

        [Option('m', "isPreviousLayerModule", Required = false, Default = false, HelpText = "Передать как базовые решения (не работает без признака includeSources)")]
        public bool IsPreviousLayerModule { get; set; }
    }

    public static void Main(string[] args)
    {
        if (args.Length < 1 || args[0].ToLower() == "help" || args[0].ToLower() == "-help")
        {
            Console.Write(Resource.HelpInfo);
            return;
        }

        Parser.Default.ParseArguments<LaunchOptions>(args)
                .WithParsed<LaunchOptions>(o =>
                {
                    string outPath = !string.IsNullOrEmpty(o.OutPath) ? o.OutPath : o.ProjectPath;

                    List<string> solutionList = new List<string>();
                    if (!string.IsNullOrEmpty(o.SolutionList))
                        solutionList.AddRange(o.SolutionList.Split('|', StringSplitOptions.TrimEntries).ToList());

                    CreateAndSavePackageXMLDocument(o.ProjectPath, outPath, solutionList, o.IncludeAssemblies, o.IsDebugPackage, o.IncludeSources, o.IsPreviousLayerModule);
                });
    }

    /// <summary>
    /// Создать и сохранить package.xml для сборки. (Перед сборкой для использования в невизуальном режиме)
    /// </summary>
    /// <param name="projectPath">Папка с всеми репозиториями. Такой обычно называют git_repository</param>
    /// <param name="outPath">Путь выгрузки готового package. (Если не указан то берётся projectPath)</param>
    /// <param name="solutionLists">Список выгружаемых решений. НЕ РЕАЛИЗОВАНО НА ДАННЫЙ МОМЕНТ</param> // TODO: Допилить при необходимости
    /// <param name="includeAssemblies">Включить в пакет исполняемые файлы</param>
    /// <param name="isDebugPackage">Передать как отладочный пакет</param>
    /// <param name="includeSources">Включить в пакет исходные коды</param>
    /// <param name="isPreviousLayerModule">Передать как базовые решения</param>
    private static void CreateAndSavePackageXMLDocument(string projectPath, string outPath, List<string> solutionList,
        bool includeAssemblies, bool isDebugPackage, bool includeSources, bool isPreviousLayerModule)
    {
        outPath = string.Format($"{outPath}\\packageGen.xml");

        isDebugPackage = includeAssemblies && isDebugPackage;
        isPreviousLayerModule = includeSources && isPreviousLayerModule;

        var itemsInfo = GetItemModuleInfo(projectPath, includeAssemblies, includeSources, isPreviousLayerModule, solutionList);

        var xmlDocument = CreatePackageXmlDocument(itemsInfo, isDebugPackage);
        xmlDocument.Save(outPath);

        Console.WriteLine(string.Format($"Package.xml created. Path: {outPath}"));
    }

    /// <summary>
    /// Получить информацию о собираемых модулях\решениях
    /// </summary>
    /// <param name="projectPath">Папка с всеми репозиториями. Такой обычно называют git_repository</param>
    /// <param name="includeAssemblies">Включить в пакет исполняемые файлы</param>
    /// <param name="includeSources">Включить в пакет исходные коды</param>
    /// <param name="isPreviousLayerModule">Передать как базовые решения</param>
    /// <returns></returns>
    private static List<ItemModuleInfo> GetItemModuleInfo(string projectPath, bool includeAssemblies, bool includeSources,
                                                          bool isPreviousLayerModule, List<string> solutionList)
    {
        var itemsModuleInfo = new List<ItemModuleInfo>();

        foreach (var mtdInfo in GetMTDFiles(projectPath)) // Сюда можно докинуть фильтрацию, если нужно будет выбирать по слоям\наименованиям\NameGuid
            itemsModuleInfo.Add(mtdInfo.ToItemModuleInfo(includeAssemblies, includeSources, isPreviousLayerModule));

        if (solutionList.Any())
        {
            List<Guid> solutionIds = itemsModuleInfo.Where(i => i.IsSolution && solutionList.Contains(i.Name)).Select(i => i.Id).ToList();
            itemsModuleInfo = itemsModuleInfo.Where(i => (solutionIds.Contains(i.SolutionId)) || solutionIds.Contains(i.Id)).ToList();
        }

        return itemsModuleInfo;
    }

    /// <summary>
    /// Создать package.xml
    /// </summary>
    /// <param name="ItemsModuleInfo">Информация о собираемых решениях\модулей</param>
    /// <param name="isDebugPackage">Передать как отладочный пакет</param>
    /// <returns></returns>
    private static XmlDocument CreatePackageXmlDocument(List<ItemModuleInfo> ItemsModuleInfo, bool isDebugPackage)
    {
        var doc = new XmlDocument();

        var packageModuleRoot = CreateRootPackageXmlElement(doc, isDebugPackage);

        foreach (var mtdInfo in ItemsModuleInfo)
            packageModuleRoot.AppendChild(mtdInfo.ToXmlElement(doc));

        return doc;
    }

    /// <summary>
    /// Создать и настроить структуру xml документа для package.xml
    /// </summary>
    /// <param name="doc">XML документ</param>
    /// <param name="isDebugPackage">Передать как отладочный пакет</param>
    /// <returns>Элемент в который нужно добавить информацию о собираемых решениях</returns>
    private static XmlElement CreateRootPackageXmlElement(XmlDocument doc, bool isDebugPackage)
    {
        // Создаем Xml заголовок.
        var xmlDeclaration = doc.CreateXmlDeclaration("1.0", string.Empty, null);
        // Добавляем заголовок перед корневым элементом.
        doc.AppendChild(xmlDeclaration);
        // Корневой элемент
        var packageRoot = doc.CreateElement("DevelopmentPackageInfo");
        // Атрибуты
        var packageRootAttXSD = doc.CreateAttribute("xmlns:xsd");
        packageRootAttXSD.Value = "http://www.w3.org/2001/XMLSchema";
        packageRoot.Attributes.Append(packageRootAttXSD);

        var packageRootAttXSI = doc.CreateAttribute("xmlns:xsi");
        packageRootAttXSI.Value = "http://www.w3.org/2001/XMLSchema-instance";
        packageRoot.Attributes.Append(packageRootAttXSI);

        doc.AppendChild(packageRoot);
        //TODO: Если будет полещно можно добавить атрибут указывающий что пакет автосгенерен нашей программой и конкретную версию 

        // Общие элементы
        // Признак сборки в режиме отладки
        var isDebugPackageXmlElement = doc.CreateElement("IsDebugPackage");
        isDebugPackageXmlElement.InnerText = isDebugPackage.ToString().ToLower();
        packageRoot.AppendChild(isDebugPackageXmlElement);

        // Root элемент для информации о модулях
        var packageModuleRoot = doc.CreateElement("PackageModules");
        packageRoot.AppendChild(packageModuleRoot);

        return packageModuleRoot;
    }

    /// <summary>
    /// Получить список mtd файлов, необходимых включить в package.xml
    /// </summary>
    /// <param name="projectPath">Папка с всеми репозиториями. Такой обычно называют git_repository</param>
    /// <returns>Список найденных mtd файлов</returns>
    private static List<MtdFileInfo> GetMTDFiles(string projectPath)
    {
        var mtdFiles = new List<MtdFileInfo>();
        foreach (var gitFolderPath in Directory.GetDirectories(projectPath))
            foreach (var solutionFolderPath in Directory.GetDirectories(gitFolderPath))
                foreach (var file in Directory.GetFiles(solutionFolderPath, "Module.mtd", SearchOption.AllDirectories)
                    .Where(x => x.Contains("Shared\\Module.mtd")) // Исключаем МТД файлы перекрытий. Пример перекрытия ...Sungero.Docflow\Module.mtd
                    .Where(x => !x.Contains("VersionData"))) // Исключаем МТД из VersionData
                    mtdFiles.Add(new MtdFileInfo(file, solutionFolderPath, gitFolderPath));

        return mtdFiles;
    }

    /// <summary>
    /// Хранит информацию о полях, необходим для package.xml
    /// </summary>
    public struct ItemModuleInfo
    {
        public Guid Id;
        public Guid SolutionId;
        public string Name;
        public string Version;
        public bool IncludeAssemblies;
        public bool IncludeSources;
        public bool IsSolution;
        public bool IsPreviousLayerModule;

        /// <summary>
        /// Преобразовать элемент в XML 
        /// </summary>
        /// <param name="doc">XML документ</param>
        /// <returns>XML вид элемента</returns>
        public XmlElement ToXmlElement(XmlDocument doc)
        {
            var itemRoot = doc.CreateElement("PackageModuleItem");

            CreateAndAppendXmlElement(doc, itemRoot, "Id", Id.ToString());
            if (SolutionId != Guid.Empty)
                CreateAndAppendXmlElement(doc, itemRoot, "SolutionId", SolutionId.ToString());
            CreateAndAppendXmlElement(doc, itemRoot, "Name", Name);
            CreateAndAppendXmlElement(doc, itemRoot, "Version", Version);
            CreateAndAppendXmlElement(doc, itemRoot, "IncludeAssemblies", IncludeAssemblies.ToString().ToLower());
            CreateAndAppendXmlElement(doc, itemRoot, "IncludeSources", IncludeSources.ToString().ToLower());
            CreateAndAppendXmlElement(doc, itemRoot, "IsSolution", IsSolution.ToString().ToLower());
            CreateAndAppendXmlElement(doc, itemRoot, "IsPreviousLayerModule", IsPreviousLayerModule.ToString().ToLower());

            return itemRoot;
        }

        /// <summary>
        /// Добавить xml дочерний элемент
        /// </summary>
        /// <param name="doc">XML документ</param>
        /// <param name="root">Родительский XML в который нужно вставить новый</param>
        /// <param name="name">Наименование элемента</param>
        /// <param name="value">Значение элемента</param>
        public void CreateAndAppendXmlElement(XmlDocument doc, XmlElement root, string name, string value)
        {
            var xmlElement = doc.CreateElement(name);
            xmlElement.InnerText = value;
            root.AppendChild(xmlElement);
        }
    }

    /// <summary>
    /// Данные по местоположению
    /// </summary>
    public struct MtdFileInfo
    {
        public string GitFolderPath;
        public string SolutionFolderPath;
        public string FilePath;

        public MtdFileInfo(string filePath, string solutionFolderPath, string gitFolderPath)
        {
            GitFolderPath = gitFolderPath;
            SolutionFolderPath = solutionFolderPath;
            FilePath = filePath;
        }

        /// <summary>
        /// Преобразовать файл из пути в ItemModuleInfo
        /// </summary>
        /// <param name="includeAssemblies">Включить в пакет исполняемые файлы</param>
        /// <param name="includeSources">Включить в пакет исходные коды</param>
        /// <param name="isPreviousLayerModule">Передать как базовые решения</param>
        /// <returns></returns>
        public ItemModuleInfo ToItemModuleInfo(bool includeAssemblies, bool includeSources, bool isPreviousLayerModule)
        {
            string jsonString = File.ReadAllText(FilePath);
            var json = JsonNode.Parse(jsonString);

            var itemModuleInfo = new ItemModuleInfo();

            itemModuleInfo.Id = Guid.Parse(json["NameGuid"].ToString());
            itemModuleInfo.Name = string.Format($"{json["CompanyCode"]}.{json["Name"]}");
            itemModuleInfo.Version = json["Version"].ToString();
            itemModuleInfo.IncludeAssemblies = includeAssemblies;
            itemModuleInfo.IncludeSources = includeSources;
            itemModuleInfo.IsPreviousLayerModule = isPreviousLayerModule;

            var dependencies = json["Dependencies"];
            var solutionDepemdemc = dependencies?.AsArray()
                ?.Where(x => x["IsSolutionModule"]?.ToString() == "true")
                ?.FirstOrDefault();

            itemModuleInfo.IsSolution = solutionDepemdemc == null;

            if (solutionDepemdemc != null)
            {
                itemModuleInfo.SolutionId = Guid.Parse(solutionDepemdemc["Id"].ToString());
            }

            return itemModuleInfo;
        }
    }
}