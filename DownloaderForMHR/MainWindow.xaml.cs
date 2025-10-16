using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Downloader;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Newtonsoft.Json;

namespace DownloaderForMHR
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 1、获取 downloadUrl 的方法：
    /// 两步法：第1步：根据 url（明慧广播） 或者 网页代码（希望之声） 获取 DownloadItem 的 intermediateUrl 和 fileName；第2步：根据 intermediateUrl  获取 downloadUrl；
    /// 一步法：通过 网页代码（当前的 优美客）先获取分辨率的连接，然后获取 playlist.m3u8 的连接，最后一次性获取 fileName 和 downloadUrl；
    /// 但是，一切都是变化的，需要不断根据实际的网页代码状况调整
    /// 2、下载的办法
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] public static extern int MessageBoxTimeoutA(IntPtr hWnd, string msg, string Caps, int type, int Id, int time);

        /////////////////////////////////////////////////////
        ///1.内部类
        #region
        [Serializable()]
        class WebSiteParseList
        {
            public int CurrentWebSiteIndex { get; set; }
            public List<WebSite> WebSites { get; set; }
            public int ThreadNum { get; set; }
            public bool UseProxy { get; set; }
            public string ProxyHost = PROXY_HOST_DEFAULT;
            public int ProxyPort = PROXY_PORT_DEFAULT;

            public List<string>? UserAgents = null;
            public WebSiteParseList(int CurrentWebSiteIndex, List<WebSite> WebSites, int ThreadNum, bool UseProxy, int ProxyPort, List<string>? userAgents)
            {
                this.CurrentWebSiteIndex = CurrentWebSiteIndex;
                this.WebSites = WebSites;
                this.ThreadNum = ThreadNum;
                this.UseProxy = UseProxy;
                this.ProxyPort = ProxyPort;
                UserAgents = userAgents;
            }
        }

        [Serializable()]
        class WebSite
        {
            public string WebSiteName { get; set; }
            public string WebSiteUrl { get; set; }
            [Newtonsoft.Json.JsonIgnore]
            public string WebSiteUrlScheme { get; set; }
            public string ParseUrlModel { get; set; }
            public ParseSelector ParseSelector { get; set; }
            public List<SiteSection> SiteSections { get; set; }
            public WebSite(string WebSiteName, string WebSiteUrl, string parseUrlModel,
                ParseSelector parseSelector, List<SiteSection> siteSections)
            {
                this.WebSiteName = WebSiteName;
                this.WebSiteUrl = WebSiteUrl;
                int pos = WebSiteUrl.IndexOf("//");
                this.WebSiteUrlScheme = pos == -1 ? "" : WebSiteUrl.Substring(0, pos);
                this.ParseUrlModel = parseUrlModel;
                this.ParseSelector = parseSelector;
                this.SiteSections = siteSections;
            }
        }

        [Serializable()]
        class ParseSelector
        {
            public string SelectorName { get; set; }
            public bool ParseByTwoSteps { get; set; }//20220608 仅仅用于检查 SelectorForUrl2 的有效性
            public bool ParseQuality { get; set; }
            public string SelectorForTitle { get; set; }
            public string SelectorForUrl1 { get; set; }//第一次解析
            public string SelectorForUrl2 { get; set; }//第二次解析，两步解析的第二步使用；一步解析用
            public string SelectorForQuality { get; set; }
            public string FileNameKeywordInclude { get; set; }
            public string FileNameKeywordExclude { get; set; }
            public string FileExtension { get; set; }
            public ParseSelector(string SelectorName, bool ParseByTwoSteps, bool ParseQuality,
                string SelectorForTitle, string SelectorForUrl1, string SelectorForUrl2, string SelectorForQuality,
                string FileNameKeywordInclude, string FileNameKeywordExclude, string FileExtension)
            {
                this.SelectorName = SelectorName;
                this.ParseByTwoSteps = ParseByTwoSteps;
                this.ParseQuality = ParseQuality;
                this.SelectorForTitle = SelectorForTitle;
                this.SelectorForUrl1 = SelectorForUrl1;
                this.SelectorForUrl2 = SelectorForUrl2;
                this.SelectorForQuality = SelectorForQuality;
                this.FileNameKeywordInclude = FileNameKeywordInclude;
                this.FileNameKeywordExclude = FileNameKeywordExclude;
                this.FileExtension = FileExtension;
            }

        }

        [Serializable()]
        class SiteSection
        {
            public string SectionName { get; set; }
            public List<SiteCategory> SiteCategories { get; set; }

            public SiteSection(string sectionName, List<SiteCategory> siteCategories)
            {
                SectionName = sectionName;
                this.SiteCategories = siteCategories;
            }
        }

        [Serializable()]
        class SiteCategory
        {
            public string CategoryName { get; set; }
            public string CategoryId { get; set; }
            public int LowerLimit { get; set; }
            public int UpperLimit { get; set; }
            public List<int> LimitRange { get; set; }
            public long TimeStamp { get; set; }

            public SiteCategory(string categoryName, string categoryId,
                int lowerLimit = 1, int upperLimit = 1, List<int>? limitRange = null, long timeStamp = 0)
            {
                this.CategoryName = categoryName;
                this.CategoryId = categoryId;
                LowerLimit = lowerLimit;
                UpperLimit = upperLimit;
                LimitRange = limitRange ?? new List<int>();
                TimeStamp = timeStamp;
            }

            public bool needRefresh()
            {
                return (DateTimeOffset.Now.ToUnixTimeSeconds() - TimeStamp) / 24 / 3600 >= 1;
            }
        }
        [Serializable()]
        class UrlItem
        {
            public string Title { get; set; }
            public string Url { get; set; }

            public UrlItem(string title, string url)
            {
                this.Title = title;
                this.Url = url;
            }
        }

        //////////////////////////////////////////
        [Serializable()]
        class DownloadHistory
        {
            public List<WebSiteDownloadHistory> WebSiteDownloadHistory { get; set; }

            public DownloadHistory(List<WebSiteDownloadHistory> webSiteDownloadHistory)
            {
                WebSiteDownloadHistory = webSiteDownloadHistory;
            }
        }

        [Serializable()]
        class WebSiteDownloadHistory
        {
            public string WebSiteName { get; set; }
            public string CurrentTargetUrl { get; set; }//干净世界时保存当前解析的连接，如果有下载任务未完成需保留下载历史时
            public CurrentSelection CurrentSelection { get; set; }
            public List<DownloadPackage> DownloadPackages { get; set; }

            public WebSiteDownloadHistory(string webSiteName, CurrentSelection currentSelection, List<DownloadPackage> downloadPackages, string currentTargetUrl = "")
            {
                WebSiteName = webSiteName;
                CurrentSelection = currentSelection;
                DownloadPackages = downloadPackages;
                CurrentTargetUrl = currentTargetUrl;
            }
        }
        [Serializable()]
        class CurrentSelection
        {
            public string SiteSectionName { get; set; }
            public string SiteCategoryName { get; set; }
            public string PageValue { get; set; }

            public CurrentSelection(string siteSectionName, string siteCategoryName, string pageValue)
            {
                this.SiteSectionName = siteSectionName;
                this.SiteCategoryName = siteCategoryName;
                PageValue = pageValue;
            }
        }
        //////////////////////////////////////////        
        [Serializable()]
        class VideoUrl
        {
            public string name { get; set; }
            public string resolution { get; set; }
            public string url { get; set; }
            public VideoUrl(string name, string resolution, string url)
            {
                this.name = name;
                this.resolution = resolution;
                this.url = url;
            }
            public override string ToString()
            {
                return $"{name} [{resolution}]: {url}";
            }
        }
        //////////////////////////////////////////
        [Serializable()]
        class DownloadItem : INotifyPropertyChanged
        {
            public int id { get; set; }
            public int DisplayId { get { return id + 1; } }
            /// <summary>
            /// TS文件专用，下载“干净世界”文件
            /// </summary>
            public int tsPosistion { get; set; } //20231106 TS文件下载，每个文件按照读取的顺序作为文件名，来保存文件，也就是说，第一填表时就确定的顺序，以后不改
            public int tsTotalNum { get; set; } //20231106 TS文件总数
            ////// 
            public string fileName { get; set; }
            public string intermediateUrl { get; set; }

            private string _downloadUrl = "";
            public string downloadUrl
            {
                get { return _downloadUrl; }
                set
                {
                    if (_downloadUrl == value)
                        return;
                    _downloadUrl = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadUrl"));
                    }
                }
            }

            private bool _isSelected;
            public bool isSelected
            {
                get { return _isSelected; }
                set
                {
                    if (_isSelected == value)
                        return;
                    _isSelected = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("isSelected"));
                    }
                }
            }

            private bool _isSelectionEnabled;
            public bool isSelectionEnabled
            {
                get { return _isSelectionEnabled; }
                set
                {
                    if (_isSelectionEnabled == value)
                        return;
                    _isSelectionEnabled = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("isSelectionEnabled"));
                    }
                }
            }

            public string folderPath;
            public string fullFileName;

            private int _downloadProgress;

            //80%
            public int downloadProgress
            {
                get { return _downloadProgress; }
                set
                {
                    if (_downloadProgress == value)
                        return;
                    _downloadProgress = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadProgress"));
                    }
                }
            }

            private string _downloadSpeed = "";

            // 200KB/s
            public string downloadSpeed
            {
                get { return _downloadSpeed; }
                set
                {
                    if (_downloadSpeed == value)
                        return;
                    _downloadSpeed = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadSpeed"));
                    }
                }
            }

            private string _fileSize = "";
            // 1.2MB/18MB
            public string fileSize
            {
                get { return _fileSize; }
                set
                {
                    if (_fileSize == value)
                        return;
                    _fileSize = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("fileSize"));
                    }
                }
            }

            public bool downloadResult; // OK

            public DownloadService? downloadService = null;

            public event PropertyChangedEventHandler? PropertyChanged;

            public DownloadItem(int id, string fileName, string fileUrl, string targetUrl, bool isSelected, string folderPath = "", int tsPosistion = 0, int tsTotalNum = 0, bool isSelectionEnabled = true, bool downloadResult = false)
            {
                this.id = id;
                this.tsPosistion = tsPosistion;
                this.tsTotalNum = tsTotalNum;
                this.fileName = fileName.Trim();
                this.intermediateUrl = fileUrl;
                this.downloadUrl = targetUrl;
                this.isSelected = isSelected;
                this.isSelectionEnabled = isSelectionEnabled;//20240227
                this.downloadResult = downloadResult;//20240227
                this.folderPath = string.IsNullOrWhiteSpace(folderPath) ? DOWNLOAD_PATH : folderPath;
                this.fullFileName = this.folderPath + "\\" + fileName;//干净世界的单独设置，避免与其它网站的冲突
                setDownloadInfo(0, "0.0KB/s", "0B", false, isSelectionEnabled);
            }

            public void setDownloadInfo(int downloadProgress, string downloadSpeed, string fileSize,/* string downloadSize, */bool downloadResult, bool isSelectionEnabled = true)
            {
                this.downloadProgress = downloadProgress;
                this.downloadSpeed = downloadSpeed;
                this.fileSize = fileSize;
                //this.downloadSize = downloadSize;
                this.downloadResult = downloadResult;
                this.isSelectionEnabled = isSelectionEnabled;
            }

            override
            public string ToString()
            {
                return string.Format("id = {0}[{9:D3}/{10:D3}], fileName = {1}, fileUrl = {2}, targetUrl = {3}, isSelected = {4}, downloadProgress = {5}, downloadSpeed = {6}, fileSize = {7}, downloadResult = {8}", id, fileName, intermediateUrl, downloadUrl, isSelected, downloadProgress, downloadSpeed, fileSize, downloadResult, tsPosistion, tsTotalNum);
            }
        }

        public class ObservableTaskProgress<T> : INotifyPropertyChanged
        {
            private T? _taskProgress;
            public T? TaskProgress
            {
                get { return _taskProgress; }
                set
                {
                    _taskProgress = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("TaskProgress"));//"Value"
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public class ObservableLastColumnWidth : INotifyPropertyChanged
        {
            private double _lastColumnWidth;
            public double LastColumnWidth
            {
                get { return _lastColumnWidth; }
                set
                {
                    _lastColumnWidth = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("LastColumnWidth"));//"Value"
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        enum AppTask
        {
            TASK_FETCH_DOWNLOAD_URL,
            TASK_DOWNLOAD,
            TASK_MERGE_TS_FILES
        }

        /*public class ObservableIsSelectionEnabled : INotifyPropertyChanged
        {

            private bool _isSelectionEnabled;
            public bool isSelectionEnabled
            {
                get { return _isSelectionEnabled; }
                set
                {
                    _isSelectionEnabled = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("isSelectionEnabled"));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }*/

        class ProxyState
        {
            public bool UseProxy;
            public string ProxyHost;
            public int ProxyPort;
            public bool IsProxyChanged;

            public ProxyState(bool useProxy = true, string proxyHost = PROXY_HOST_DEFAULT, int proxyPort = PROXY_PORT_DEFAULT, bool isProxyChanged = false)
            {
                UseProxy = useProxy;
                ProxyHost = proxyHost;
                ProxyPort = proxyPort;
                IsProxyChanged = isProxyChanged;
            }
        }
        #endregion

        /////////////////////////////////////////////////////
        ///2.参量
        #region
        private const string WEB_SITE_NAME_MHR = "明慧广播电台";
        private const string WEB_SITE_NAME_MHR_IN_SOH = "明慧广播节目下载（希望之声提供）";
        private readonly List<string> WEB_SITE_NAMES_SUPPORTED = new List<string>() { WEB_SITE_NAME_MHR, WEB_SITE_NAME_MHR_IN_SOH };
        private readonly Dictionary<string, string> IDS_OF_MHR = new Dictionary<string, string>() {
             {"duanbuoshouting", "短波收听" },
             {"xiulianyuandi", "修炼园地" },
             {"xinwenshishi", "新闻时事" },
             {"yinyuexinshang", "音乐欣赏" },
        };
        private readonly List<string> EMPTY_STRING_LIST = new List<string>();

        private static string APP_PATH = Directory.GetCurrentDirectory();
        private string SETTINGS_JSON_FILE = APP_PATH + @"\settings.json";
        private string DOWNLOAD_HISTORY_JSON_FILE = APP_PATH + @"\download_history.json";
        ObservableCollection<DownloadItem> downloadItemList = new ObservableCollection<DownloadItem>();
        List<DownloadItem> downloadList = new List<DownloadItem>();
        ObservableTaskProgress<double> taskProgress = new ObservableTaskProgress<double>();
        private const string USER_AGENT_DEFAULT = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36";
        private const string PROXY_HOST_DEFAULT = "127.0.0.1";
        private const int PROXY_PORT_DEFAULT = 8580;

        WebSiteParseList? webSiteParseList;
        WebSite? currentWebSite;
        private string oldTaskTarget = "";
        DownloadHistory? downloadHistory;
        WebSiteDownloadHistory? webSiteDownloadHistory;

        //private int oldCmbPageValueIndex = -1;
        private bool oldUseProxy = false;
        private int oldProxyPort = 0;
        private ProxyState proxyState = new ProxyState();

        int totalDownloadNum = 0;
        int countCompleted = 0;

        private string userAgent = USER_AGENT_DEFAULT;
        bool test = false; //!!!

        //20220605
        private static string DOWNLOAD_PATH = APP_PATH + @"\下载";
        private readonly int DEFAULT_THREAD_NUM = 3;
        private readonly List<int> threadNums = new List<int>() { 1, 2, 3, 6, 9 };
        private AtomicBoolean abFetchDownloadUrl = new AtomicBoolean(false);
        private AtomicBoolean abStartDownload = new AtomicBoolean(false);
        private AtomicBoolean abMergeTSFiles = new AtomicBoolean(false);
        private int currentTaskId = 0;
        private object obj = new object();
        private HashSet<string> errors = new HashSet<string>();

        //20231123
        CancellationTokenSource cancellationToken = new CancellationTokenSource();

        #endregion

        /////////////////////////////////////////////////////
        ///3.初始化
        #region
        public MainWindow()
        {
            InitializeComponent();

            ShowTaskInfoOnUI("正在初始化...");
            RegisterDefaultBooleanConverter();
            this.DataContext = taskProgress;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //1.
            InitView();
            //2.
            InitDownloadPath();
            //3.
            ParseMainJsonAsync();
        }

        private void RegisterDefaultBooleanConverter()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new BooleanJsonConverter()
                }
            };
        }

        private void InitView()
        {
            myWindows.Title = "明慧广播批量下载器" + getApplicationVersion();
            //1.
            LvDownloadItem.ItemsSource = downloadItemList;
            //2.
            threadNums.ForEach(num => cmbThreadNum.Items.Add(num));
            //3.
            btnTest.Visibility = test ? Visibility.Visible : Visibility.Collapsed;

        }

        private string getApplicationVersion()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            Version? version = assembly.GetName().Version;
            if (version == null) return "";
            return $" V{version.Major:D}.{version.Minor:D}.{version.Build:D4}.{version.Revision:D4}";
        }

        private async void ParseMainJsonAsync()
        {
            try
            {
                Task<WebSiteParseList?> task = Task<WebSiteParseList>.Run(() =>
                {
                    return ParseMainJson();
                });
                webSiteParseList = await task;
                if (webSiteParseList == null || webSiteParseList.WebSites.Count == 0)
                {
                    if (MessageBoxError("文件格式错误或者没有有效的设置信息！"))
                    {
                        Environment.Exit(0);
                    }
                    return;
                }
                Task<DownloadHistory?> task2 = Task<DownloadHistory>.Run(() =>
                {
                    return ParseDownloadHistoryJson();
                });
                downloadHistory = await task2;
                ShowTaskInfoOnUI("准备就绪，欢迎使用本程序！");
                //1.初始化站点列表
                webSiteParseList.WebSites.ForEach(webSite => cmbWebSite.Items.Add(webSite.WebSiteName));
                //2.初始化线程数量
                InitThreadNumCombox(webSiteParseList.ThreadNum);
                ckbUseProxy.IsChecked = webSiteParseList.UseProxy;
                InitProxy(webSiteParseList);
                tbProxy.Text = $"{webSiteParseList.ProxyHost}:{webSiteParseList.ProxyPort}";
                //
                if (webSiteParseList.UserAgents != null && webSiteParseList.UserAgents.Count > 0)
                {
                    userAgent = webSiteParseList.UserAgents[new Random().Next(webSiteParseList.UserAgents.Count)];
                }
                Log($"App UserAgent = {userAgent}");
                //3.【重要】最后设定SelectedIndex，因为会引起 cmbWebSiteNames_SelectionChanged 一些列变动，包括SaveMainJson()
                cmbWebSite.SelectedIndex = webSiteParseList.CurrentWebSiteIndex;
            }
            catch (Exception e)
            {
                Log(e.Message);
                if (MessageBoxError($"初始化网站及其模板出错！\n详情：{e.Message}"))
                {
                    Environment.Exit(0);
                }
            }
        }

        private WebSiteParseList? ParseMainJson()
        {
            try
            {
                if (!File.Exists(SETTINGS_JSON_FILE))
                    return null;
                var settings = new JsonSerializerSettings();
                //settings.Converters.Add(new StorageConverter());
                string jsonText = File.ReadAllText(SETTINGS_JSON_FILE);
                WebSiteParseList? list = JsonConvert.DeserializeObject<WebSiteParseList>(jsonText);//, settings);
                AdjustIndex(list, true);
                Log("ParseMainJson OK...");
                return list;
            }
            catch (Exception e)
            {
                Log("ParseMainJson ERROR: " + e.Message);
                return null;
            }
        }

        private DownloadHistory? ParseDownloadHistoryJson()
        {
            try
            {
                if (!File.Exists(DOWNLOAD_HISTORY_JSON_FILE)) return null;

                var settings = new JsonSerializerSettings();
                //settings.Converters.Add(new StorageConverter());
                string jsonText = File.ReadAllText(DOWNLOAD_HISTORY_JSON_FILE);
                DownloadHistory? downloadHistory = JsonConvert.DeserializeObject<DownloadHistory>(jsonText);//, settings);
                Log("ParseDownloadHistoryJson OK...");
                return downloadHistory;
            }
            catch (Exception e)
            {
                Log("ParseDownloadHistoryJson ERROR: " + e.Message);
                return null;
            }
        }

        private void InitDownloadPath()
        {
            try
            {
                if (!Directory.Exists(DOWNLOAD_PATH))
                {
                    Directory.CreateDirectory(DOWNLOAD_PATH);
                }
            }
            catch (Exception ex)
            {
                Log($"InitDownloadPath： 创建 {DOWNLOAD_PATH} 失败！详情：{ex.Message}");
            }
        }

        private void InitProxy(WebSiteParseList webSite)
        {
            //1.
            proxyState.UseProxy = webSite.UseProxy;
            proxyState.IsProxyChanged = false;
            proxyState.ProxyHost = webSite.ProxyHost;
            proxyState.ProxyPort = webSite.ProxyPort;
            //2.
            string errInfo = "";
            if (!IsProxyHostValid(webSite.ProxyHost))
            {
                errInfo += "主机";
            }

            if (!IsProxyPortValid(webSite.ProxyPort))
            {
                if (string.IsNullOrEmpty(errInfo))
                    errInfo += "端口";
                else
                    errInfo += "和端口";
            }
            if (string.IsNullOrEmpty(errInfo)) return;
            MessageBoxError("代理的" + errInfo + "设置错误！请修改。");
        }

        private async Task<List<SiteSection>> ParseMHRadio(string url)
        {
            List<SiteSection> siteSections = new List<SiteSection>();

            if (!ValidateProxy()) return siteSections;
            try
            {
                var doc = await FetchHtmlDocumentAsync(url);
                if (doc == null) return siteSections;
                string REG_CATEGORY_ID = @"/showcategory/(\d+)/\d+.html";
                MatchCollection matchCollection;
                string categoryUrl = "", categoryId = "", categoryName = "";
                foreach (string id in IDS_OF_MHR.Keys)
                {
                    string sectionName = IDS_OF_MHR[id];
                    List<SiteCategory> siteCategories = new List<SiteCategory>();
                    Log($">> {sectionName}");
                    doc.DocumentNode.SelectSingleNode($"//div[@id='{id}']/div[@class='categorylist']").Descendants("a").AsParallel().ToList().ForEach(ac =>
                    {
                        categoryUrl = ac.GetAttributeValue("href", "");
                        categoryName = ac.InnerText;
                        matchCollection = Regex.Matches(categoryUrl, REG_CATEGORY_ID);
                        if (matchCollection.Count > 0)
                        {
                            categoryId = matchCollection[0].Groups[1].ToString();
                        }
                        if (categoryUrl.Contains("/showcategory/") && !string.IsNullOrEmpty(categoryId))
                        {
                            Log($"   {categoryUrl}, {categoryName}, {categoryId}");
                            siteCategories.Add(new SiteCategory(categoryName, categoryId));
                        }
                    });
                    siteSections.Add(new SiteSection(sectionName, siteCategories));
                }
                return siteSections;
            }
            catch (Exception e)
            {
                Log($"ParseSiteSectionsForMHR: 出错，详情：{e.Message}");
                MessageBoxError($"更新页序信息出错，详情：{e.Message}");
                return siteSections;
            }
        }

        private async Task<int> ParsePageValueUpperLimitForMHR(string url)
        {
            if (!ValidateProxy()) return 1;
            try
            {
                var doc = await FetchHtmlDocumentAsync(url);
                if (doc == null) return -1;
                var node1 = doc.QuerySelectorAll("#navigationbar a").ToList().Find(node => node.InnerText == "末页");
                if (node1 == null)
                    node1 = doc.QuerySelectorAll("#navigationbar a").ToList().Last();
                var href = node1.GetAttributeValue("href", "");
                if (int.TryParse(href.Substring(href.LastIndexOf("/") + 1).Replace(".html", ""), out int upperLimit))
                    return upperLimit;
                else
                    return 1;
            }
            catch (Exception e)
            {
                Log($"ParseSiteSectionsForMHR: 出错，详情：{e.Message}");
                MessageBoxError($"更新页序信息出错，详情：{e.Message}");
                return 1;
            }
        }
        private async Task<List<SiteSection>?> ParseSiteSectionsForMHRInSOH(string url)
        {
            if (!ValidateProxy()) return null;
            try
            {
                var doc = await FetchHtmlDocumentAsync(url);
                if (doc == null) return null;
                var body = doc.QuerySelector("body").InnerHtml;
                string[] spliters = new string[] { "<h2><font color=\"green\">" };
                var fonts = body.Split(spliters, StringSplitOptions.RemoveEmptyEntries);
                var sections = new List<SiteSection>();
                fonts.ToList().ForEach(font =>
                {
                    Log(spliters[0] + font);
                    doc.LoadHtml(spliters[0] + font);
                    var sectionName = doc.QuerySelector("font[color='green']").InnerText.ToString();
                    List<SiteCategory> siteCategories = new List<SiteCategory>();
                    var dateTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                    doc.QuerySelectorAll("h4").ToList().ForEach(h4 =>
                    {
                        var categoryName = h4.QuerySelector("font").InnerText.ToString();
                        var urlItems = new List<UrlItem>();
                        h4.QuerySelectorAll("a").ToList().ForEach(a =>
                        {
                            var href = a.GetAttributeValue("href", "").Replace("/mhradio/", "");
                            var name = a.InnerText.ToString();
                            urlItems.Add(new UrlItem(name, href));
                        });
                        if (urlItems.Count == 0) return;
                        var categoryId = urlItems[0].Url.Substring(0, urlItems[0].Url.LastIndexOf("/"));
                        List<int> years = urlItems.ConvertAll(item =>
                        {
                            if (int.TryParse(item.Title.Trim(), out int tmp))
                                return tmp;
                            else
                                return 0;
                        });
                        years.RemoveAll(item => item == 0);
                        siteCategories.Add(new SiteCategory(categoryName, categoryId, limitRange: years, timeStamp: dateTime));
                    });
                    if (siteCategories.Count == 0) return;
                    sections.Add(new SiteSection(sectionName, siteCategories));
                });
                return sections;
            }
            catch (Exception e)
            {
                Log("ParseSiteSectionsForMHRInSOH: " + e.Message);
                MessageBoxError($"更新年份信息出错，详情：{e.Message}");
                return null;
            }

        }
        #endregion

        /////////////////////////////////////////////////////
        ///4.退出数据保存
        #region
        private void Window_Closed(object sender, EventArgs e)
        {
            CheckProxyStateOnCloseApp();
            SaveMainJson();
        }

        /// <summary>
        /// 代理的保存，为上一次正确运行的代理状态参数
        /// </summary>
        private void SaveMainJson()
        {
            //注意2个index要加 1
            try
            {
                //1.
                if (webSiteParseList == null) return;
                AdjustIndex(webSiteParseList, false);
                webSiteParseList.ThreadNum = threadNums[cmbThreadNum.SelectedIndex];
                webSiteParseList.UseProxy = (bool)(ckbUseProxy.IsChecked ?? false);
                string json = JsonConvert.SerializeObject(webSiteParseList, Formatting.Indented);
                File.WriteAllText(SETTINGS_JSON_FILE, json);
                //20220609 保存完成后修改回来,当然也可以用序列和反序列化一个新的实体webSiteParseList，而不改当前的
                //WebSiteParseList newWebSiteParseList = JsonConvert.DeserializeObject<WebSiteParseList>(JsonConvert.SerializeObject(webSiteParseList));
                AdjustIndex(webSiteParseList, true);
                //2.
                //2.1 检查是否需要保存
                if (currentWebSite == null) return;
                if (cmbSiteSection.SelectedItem == null || cmbSiteCategory.SelectedItem == null) return;
                List<DownloadPackage> downloadPackages = PrepareDownloadPackageData();
                WebSiteDownloadHistory? webSiteHistory;
                if (downloadPackages.Count == 0)
                {
                    if (downloadHistory == null) return;
                    webSiteHistory = downloadHistory.WebSiteDownloadHistory.Find(item => item.WebSiteName == currentWebSite.WebSiteName);
                    if (webSiteHistory == null) return;
                    downloadHistory.WebSiteDownloadHistory.Remove(webSiteHistory);
                    if (downloadHistory.WebSiteDownloadHistory.Count == 0)
                    {
                        json = "";
                    }
                    else
                    {
                        json = JsonConvert.SerializeObject(downloadHistory, Formatting.Indented);
                    }
                    File.WriteAllText(DOWNLOAD_HISTORY_JSON_FILE, json);
                    return;
                }
                //2.2 检查构造 webSiteHistory 和 currentSelection
                CurrentSelection currentSelection = new CurrentSelection("", "", "");
                if (downloadHistory == null)
                {
                    webSiteHistory = new WebSiteDownloadHistory(currentWebSite.WebSiteName, currentSelection, downloadPackages);
                    downloadHistory = new DownloadHistory(new List<WebSiteDownloadHistory> { webSiteHistory });
                }
                else
                {
                    webSiteHistory = downloadHistory.WebSiteDownloadHistory.Find(item => item.WebSiteName == currentWebSite.WebSiteName);
                    if (webSiteHistory == null)
                    {
                        webSiteHistory = new WebSiteDownloadHistory(currentWebSite.WebSiteName, currentSelection, downloadPackages);
                        downloadHistory.WebSiteDownloadHistory.Add(webSiteHistory);
                    }
                    else
                    {
                        webSiteHistory.DownloadPackages = downloadPackages;
                    }
                }
                //2.3
                webSiteHistory.CurrentSelection.SiteSectionName = cmbSiteSection.SelectedItem.ToString() ?? "";//20231123
                webSiteHistory.CurrentSelection.SiteCategoryName = cmbSiteCategory.SelectedItem.ToString() ?? "";//20231123
                var siteCategory = currentWebSite.SiteSections[cmbSiteSection.SelectedIndex].SiteCategories[cmbSiteCategory.SelectedIndex];
                var pageValue = cmbPageValue.SelectedItem == null ? siteCategory.LowerLimit.ToString() : cmbPageValue.SelectedItem.ToString();
                webSiteHistory.CurrentSelection.PageValue = pageValue ?? "";
                json = JsonConvert.SerializeObject(downloadHistory, Formatting.Indented);
                File.WriteAllText(DOWNLOAD_HISTORY_JSON_FILE, json);
            }
            catch (Exception e1)
            {
                Log(e1.Message);
            }
        }

        private List<DownloadPackage> PrepareDownloadPackageData()//20240225 修改
        {
            var empty = new List<DownloadPackage>();
            if (currentWebSite == null || downloadItemList.Count == 0) return empty;

            List<DownloadItem> savedItems;
            savedItems = downloadItemList.ToList().FindAll((DownloadItem item) =>
                                /*item.downloadService != null && item.downloadService.Package != null && */!item.downloadResult);
            return CollectDownloadPackage(savedItems);
        }

        private List<DownloadPackage> CollectDownloadPackage(List<DownloadItem> list)
        {
            var empty = new List<DownloadPackage>();
            try
            {
                if (list.Count == 0)
                {
                    Log("CollectDownloadPackage(): 没有需要保存的数据！");
                    return empty;
                }

                return list.ConvertAll(item =>
                {
                    if (item.downloadService != null && item.downloadService.Package != null)
                    {
                        item.downloadService.Package.FileName = GetRelativePath(item.fullFileName);//20251014[1] 获取保存的相对路径
                        return item.downloadService.Package;
                    }
                    else
                    {
                        var downloadPackage = new DownloadPackage();
                        downloadPackage.IsSaving = false;
                        downloadPackage.IsSaveComplete = item.downloadResult;
                        downloadPackage.SaveProgress = item.downloadProgress;
                        downloadPackage.Urls = new String[] { item.downloadUrl };
                        long.TryParse(item.fileSize, out long totalFileSize);
                        downloadPackage.TotalFileSize = totalFileSize;
                        downloadPackage.FileName = GetRelativePath(item.fullFileName);
                        downloadPackage.Chunks = null;
                        return downloadPackage;
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"CollectDownloadPackage() 出错：{ex.Message}");
                return empty;
            }
        }

        private void RemoveCurrentWebSiteDownloadHistoryAndSave()
        {
            try
            {
                if (downloadHistory == null || downloadHistory.WebSiteDownloadHistory == null || currentWebSite == null) return;
                WebSiteDownloadHistory? webSiteHistory = downloadHistory.WebSiteDownloadHistory.Find(item => item.WebSiteName == currentWebSite.WebSiteName);
                if (webSiteHistory == null) return;
                downloadHistory.WebSiteDownloadHistory.Remove(webSiteHistory);

                string json = "";
                if (downloadHistory.WebSiteDownloadHistory.Count > 0)
                {
                    json = JsonConvert.SerializeObject(downloadHistory, Formatting.Indented);
                }
                File.WriteAllText(DOWNLOAD_HISTORY_JSON_FILE, json);
            }
            catch (Exception e1)
            {
                Log("RemoveCurrentWebsiteHistoryAndSave: " + e1.Message);
            }
        }

        #endregion

        /////////////////////////////////////////////////////
        ///5.工具函数
        #region

        private async Task<bool> InitCmbWebSite()
        {
            //1.备份 DownloadPackages，在未修改2中数据及其系列数据变动前保存一次
            //if (cmbSiteSection.Items.Count > 0)//简单判断下是否初始状态，TODO 剔除板块下面栏目为空的，但是不需要考虑这个问题，不会出现
            SaveMainJson();
            //2.清零
            downloadItemList.Clear();
            cmbSiteSection.Items.Clear();
            cmbSiteCategory.Items.Clear();
            cmbPageValue.Items.Clear();
            tbTaskTarget.Text = "";
            //oldCmbPageValueIndex = -1;

            //4.初始化 currentWebSite，检查 
            if (webSiteParseList == null) return false;
            webSiteParseList.CurrentWebSiteIndex = cmbWebSite.SelectedIndex;
            currentWebSite = webSiteParseList.WebSites[webSiteParseList.CurrentWebSiteIndex];
            //4.1.初始化部分按钮或组合
            dockPanelCmb1.Visibility = Visibility.Visible;
            /*if (ENABLE_VIDEO_CONVERTOR_BUTTON)
            {
                btnMergeTsFiles.Visibility = Visibility.Collapsed;
            }*/
            //IsSelectionEnabled.isSelectionEnabled = true;//20240227
            chkbSelectAll.IsEnabled = true;//20240227
            btnStartDownload.ToolTip = null;//20240227
            //4.2
            webSiteDownloadHistory = downloadHistory?.WebSiteDownloadHistory.Find(item => item.WebSiteName == currentWebSite.WebSiteName);
            lbPageValue.Content = currentWebSite.WebSiteName == WEB_SITE_NAME_MHR ? "页序" : "年份";
            if (!CheckParseSelectorsValidity()) return false;
            //5.
            if (currentWebSite.SiteSections == null || currentWebSite.SiteSections.Count == 0 ||
               currentWebSite.SiteSections[0].SiteCategories == null || currentWebSite.SiteSections[0].SiteCategories.Count == 0 ||
               currentWebSite.SiteSections[0].SiteCategories.Any(item => item.needRefresh()))
            {
                if (currentWebSite.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH)
                {
                    while (!await RefreshPageValueForMultipleYears(currentWebSite))
                    {//20250424
                        if (!MessageBoxErrorWith2Btns($"更新“{currentWebSite.WebSiteName}”的年份信息出错！请检查网络状态。如需再次尝试获取，请选择“确认”按钮。"))
                            return false;
                    }
                }
                else //WEB_SITE_NAME_MHR
                {
                    while (!await RefreshMhRadio(currentWebSite))
                    {//20250424
                        if (!MessageBoxErrorWith2Btns($"更新明慧广播信息出错！请检查网络状态。如需再次尝试获取，请选择“确认”按钮。"))
                            return false;
                    }
                }

            }
            //6.初始化 cmbSiteSection
            cmbSiteSection.Items.Clear();
            currentWebSite.SiteSections?.ForEach(item => cmbSiteSection.Items.Add(item.SectionName));
            cmbSiteSection.SelectedIndex = GetCurrentSiteSectionIndex();
            return true;
        }

        private int GetCurrentSiteSectionIndex()
        {
            if (cmbSiteSection.Items.Count == 0) return -1;
            if (webSiteDownloadHistory == null ||
                webSiteDownloadHistory.CurrentSelection == null ||
                webSiteDownloadHistory.CurrentSelection.SiteSectionName == null)
                return 0;
            var name = webSiteDownloadHistory.CurrentSelection.SiteSectionName;
            var index = cmbSiteSection.Items.IndexOf(name);
            return index == -1 ? 0 : index;
        }

        private int GetCurrentSiteCategoryIndex()
        {
            if (cmbSiteCategory.Items.Count == 0) return -1;
            if (webSiteDownloadHistory == null ||
                webSiteDownloadHistory.CurrentSelection == null ||
                webSiteDownloadHistory.CurrentSelection.SiteCategoryName == null)
                return 0;
            var name = webSiteDownloadHistory.CurrentSelection.SiteCategoryName;
            var index = cmbSiteCategory.Items.IndexOf(name);
            return index == -1 ? 0 : index;
        }

        private int GetCurrentPageValueIndex()
        {

            if (currentWebSite == null || cmbPageValue.Items.Count == 0) return -1;
            if (webSiteDownloadHistory == null ||
                webSiteDownloadHistory.CurrentSelection == null ||
                webSiteDownloadHistory.CurrentSelection.PageValue == null)
            {
                if (currentWebSite.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH) //按年
                    return cmbPageValue.Items.Count - 1;
                else //按页
                    return 0;
            }

            int index;
            //20240228 居然json回来时是 int 而非 string
            if (currentWebSite.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH)
            {
                int.TryParse(webSiteDownloadHistory.CurrentSelection.PageValue, out int value);
                index = cmbPageValue.Items.IndexOf(value);
            }
            else //WEB_SITE_NAME_MHR
            {
                index = cmbPageValue.Items.IndexOf(webSiteDownloadHistory.CurrentSelection.PageValue);
            }
            return index == -1 ? 0 : index;
        }

        private WebSiteDownloadHistory? FindWebSiteDownloadHistory()
        {
            if (downloadHistory == null || currentWebSite == null) return null;
            if (cmbSiteSection.SelectedItem == null || cmbSiteCategory.SelectedItem == null || cmbPageValue.SelectedItem == null)
                return null;
            return downloadHistory.WebSiteDownloadHistory.Find(history =>
                history.WebSiteName == currentWebSite.WebSiteName &&
                history.CurrentSelection.SiteSectionName == cmbSiteSection.SelectedItem.ToString() &&
                history.CurrentSelection.SiteCategoryName == cmbSiteCategory.SelectedItem.ToString() &&
                history.CurrentSelection.PageValue == cmbPageValue.SelectedItem.ToString());
        }

        private void InitWebSiteDownloadHistory()
        {
            var websitDownloadHistory = FindWebSiteDownloadHistory();
            if (websitDownloadHistory == null || websitDownloadHistory.DownloadPackages == null ||
                websitDownloadHistory.DownloadPackages.Count == 0)
                return;
            InitSavedDownloadPackages(websitDownloadHistory.DownloadPackages);
            CheckCheckedState();
        }

        private async Task<bool> RefreshMhRadio(WebSite webSite)
        {
            var dialog = ShowProgressDialog($"正在获取明慧广播信息，请稍候...");
            if (webSite.WebSiteName == WEB_SITE_NAME_MHR)//MHR
            {
                var siteSections = await ParseMHRadio(webSite.WebSiteUrl);
                if (siteSections.Count == 0)
                {
                    dialog.CloseMe();
                    return false;//20231109 获取失败
                }
                webSite.SiteSections = siteSections;
                cmbPageValue.SelectedIndex = GetCurrentPageValueIndex();
            }
            //currentTime = DateTime.Now;
            //Log($"RefreshPageValueForMultiplePage[4] >> {currentTime.Minute}.{currentTime.Second}.{currentTime.Millisecond}...");
            dialog.CloseMe();
            return true;
        }

        private async Task<bool> RefreshPageValueForMultiplePage(SiteCategory siteCategory)
        {
            var dialog = ShowProgressDialog($"正在获取“{siteCategory.CategoryName}”的页序信息，请稍候...");
            //var currentTime = DateTime.Now;
            //Log($"RefreshPageValueForMultiplePage[1] >> {currentTime.Minute}.{currentTime.Second}.{currentTime.Millisecond}...");
            if (currentWebSite?.WebSiteName == WEB_SITE_NAME_MHR)//MHR
            {
                //currentTime = DateTime.Now;
                //Log($"RefreshPageValueForMultiplePage[2] >> {currentTime.Minute}.{currentTime.Second}.{currentTime.Millisecond}...");
                var url = currentWebSite.ParseUrlModel.Replace("{1}", siteCategory.CategoryId).Replace("{2}", "1");
                var upperLimit = await ParsePageValueUpperLimitForMHR(url);
                if (upperLimit == -1)
                {
                    dialog.CloseMe();
                    return false;//20231109 获取失败
                }
                //await Task.Delay(2000);
                //currentTime = DateTime.Now;
                //Log($"RefreshPageValueForMultiplePage[3] >> {currentTime.Minute}.{currentTime.Second}.{currentTime.Millisecond}...");
                if (upperLimit > siteCategory.UpperLimit)
                {
                    //if (!int.TryParse(cmbPageValue.SelectedItem.ToString(), out int currentPageValue))
                    //int    currentPageValue = GetCurrentPageValueIndex();
                    siteCategory.UpperLimit = upperLimit;
                    siteCategory.TimeStamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                }
                else
                {
                    siteCategory.TimeStamp = 0;//20220625
                }
                cmbPageValue.Items.Clear();
                for (int i = siteCategory.LowerLimit; i <= upperLimit; i++)
                {
                    cmbPageValue.Items.Add(i.ToString());
                }
                cmbPageValue.SelectedIndex = GetCurrentPageValueIndex();
            }
            //currentTime = DateTime.Now;
            //Log($"RefreshPageValueForMultiplePage[4] >> {currentTime.Minute}.{currentTime.Second}.{currentTime.Millisecond}...");
            dialog.CloseMe();
            return true;
        }

        private async Task<bool> RefreshPageValueForMultipleYears(WebSite webSite)
        {
            //if (currentWebSite == null) return false;
            var dialog = ShowProgressDialog($"正在获取“{webSite.WebSiteName}”的年份信息，请稍候...");
            if (webSite.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH)//MHR in SOH
            {
                var siteSections = await ParseSiteSectionsForMHRInSOH(webSite.WebSiteUrl);
                if (siteSections != null && siteSections.Count > 0)
                    webSite.SiteSections = siteSections;
                else
                {
                    dialog.CloseMe();
                    return false;//20250424
                }
            }
            dialog.CloseMe();
            return true;
        }

        private async Task<HtmlDocument?> FetchHtmlDocumentAsync(string url, bool isMhrFromSoh = false)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            string htmlCode = await GetHtmlCodeAsync(url);
            if (String.IsNullOrEmpty(htmlCode)) return null;
            if (isMhrFromSoh)
            {
                //20240223 根据希望之声提供的明慧广播“天音净乐”2023错误格式做修正，清除类似<H3></H3>样式;
                htmlCode = Regex.Replace(htmlCode, @"</?[hH]{1}\d{1}>", "");
            }
            doc.LoadHtml(htmlCode);
            return doc;
        }

        private async Task<string> GetHtmlCodeAsync(string url)
        {
            return await FetchHtmlAsync(url);
        }

        private Task<string> FetchHtmlAsync(string url)
        {
            var t = Task.Run(() =>
            {
                return FetchHtml(url);
            });
            return t;
        }

        private string FetchHtml(string url)
        {
            try
            {
                //1.
                //2.
                HttpWebRequest hwr = (HttpWebRequest)HttpWebRequest.Create(url);
                hwr.Headers.Add("User-Agent", userAgent);//20240912
                //设置下载请求超时为200秒
                hwr.Timeout = 15000;
                Log($"Proxy: useProxy = {proxyState.UseProxy}, host = {proxyState.ProxyHost}, port = {proxyState.ProxyPort}");
                if (proxyState.UseProxy)
                    hwr.Proxy = new WebProxy(proxyState.ProxyHost, proxyState.ProxyPort);
                //得到HttpWebResponse对象
                HttpWebResponse hwp = (HttpWebResponse)hwr.GetResponse();
                //根据HttpWebResponse对象的GetResponseStream()方法得到用于下载数据的网络流对象
                Stream ss = hwp.GetResponseStream();
                var ms = StreamToMemoryStream(ss);
                string html = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                //log("url ==> \n" + html);
                return html;
            }
            catch (Exception e)
            {
                Log(e.Message);
                return "";
            }
        }

        private MemoryStream StreamToMemoryStream(Stream inStream)
        {
            MemoryStream outStream = new MemoryStream();
            const int buffLen = 4096;
            byte[] buffer = new byte[buffLen];
            int count = 0;
            while ((count = inStream.Read(buffer, 0, buffLen)) > 0)
            {
                outStream.Write(buffer, 0, count);
            }
            return outStream;
        }

        private string GetParentUrlPath(string url, string separator)
        {
            int position = url.LastIndexOf(separator);
            if (position == -1) return url;
            return url.Substring(0, position + 1);
        }

        private string GetFileName(string url, string separator)
        {
            int position = url.LastIndexOf(separator);
            if (position == -1) return "";
            return url.Substring(position + 1);
        }

        private bool StringContainIgnoreCase(string str, string target)
        {
            if (string.IsNullOrWhiteSpace(str)) return false;
            return str.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private bool IsNumber(string text)
        {
            int number;

            //Allowing only numbers
            if (!(int.TryParse(text, out number)))
            {
                return false;
            }
            return true;
        }

        private string CalcMemoryMensurableUnit(double bytes)
        {
            double kb = bytes / 1024; // · 1024 Bytes = 1 Kilobyte 
            double mb = kb / 1024; // · 1024 Kilobytes = 1 Megabyte 
            double gb = mb / 1024; // · 1024 Megabytes = 1 Gigabyte 
            double tb = gb / 1024; // · 1024 Gigabytes = 1 Terabyte 

            string result =
                tb > 1 ? $"{tb:0.0}TB" : //0.##
                gb > 1 ? $"{gb:0.0}GB" : //0.##
                mb > 1 ? $"{mb:0.0}MB" : //0.##
                kb > 1 ? $"{kb:0.0}KB" : //0.##
                $"{bytes:0.0}B";

            result = result.Replace("/", ".");
            return result;
        }

        public void Log(string msg)
        {
            //Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

        private string PatchTitle(string title)
        {
            title = title.Trim();
            int pos = title.IndexOf('|');
            if (pos > 0)
                title = title.Substring(0, pos);
            //20220609 剔除里面看不见的不合规则的字符（实际却是不可见！！！）
            //20251014-3
            title = Regex.Replace(title, @"[\\/:*?<>|]", " ");//20250815 多个.导致文件名以至于下载出现错误
            title = title.Replace("｜", " ").Replace("\n", " ").Replace("\r", " ").Replace("\"", " ").Replace("\r\n", " ");
            //20240304 处理连续多个无意义空格的问题；20250815 多个.导致文件名以至于下载出现错误
            return Regex.Replace(title, @"[. ]{2,}", " ").Trim();
        }

        private string ComputeMD5(string source)
        {
            using (var md5 = MD5.Create())
            {
                var data = md5.ComputeHash(Encoding.UTF8.GetBytes(source));
                StringBuilder builder = new StringBuilder();
                // 循环遍历哈希数据的每一个字节并格式化为十六进制字符串 
                for (int i = 0; i < data.Length; i++)
                {
                    builder.Append(data[i].ToString("X2"));
                }
                string result = builder.ToString().Substring(8, 16);
                Log("方式4：" + result);
                return result;
            }
        }

        /**
         * [20251014]2 修改 
         **/
        private string GetRelativePath(string fullFileName)
        {
            string r = fullFileName.Replace(DOWNLOAD_PATH, "");            
            if (r.StartsWith('\\')) r = r.Substring(1);
            return r;
        }
        #endregion

        /////////////////////////////////////////////////////
        ///6.获取下载链接
        #region
        //link: 如果 ParseByUrl 为 T，则为网页链接url; 否则，为文件路径path。
        private void FecthDownloadUrls()
        {
            //1.检查网站的支持情况
            if (!IsWebSiteSupported()) return;
            //2.检查网络状态
            if (!ValidateProxy()) return;
            //3.检查要解析的连接或文件
            string url = tbTaskTarget.Text.Trim();
            if (!CheckTbWebUrl(url)) return;
            //4.检查当前完成状态
            var list = CheckDownloadUrlState(url);
            //4.1 [1]获取完成，不需要从新获取
            if (list == null) return;
            CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, true);
            //4.2 [2]继续完成未完成的(TS的不存在这种情况，属于一次获取，询问质量，一次下载和解析完成)
            if (list.Count > 0)
            {
                FetchUnfinishedDownloadUlrs(list);
                return;
            }
            //4.3 [3]重新下载
            //获取或从新下载（更新），主要需要剔除【 有标题 而 无中间连接】 的无效item ，
            //不过一般网页不会出错，不会出现 无效item。
            downloadItemList.Clear();
            if (currentWebSite?.WebSiteName == WEB_SITE_NAME_MHR)
            {
                FetchDownloadUrlsForMHR(url);
            }
            else if (currentWebSite?.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH)
            {
                FetchDownloadUrlsForMHRInSOH(url);
            }
        }

        private void FetchUnfinishedDownloadUlrs(List<DownloadItem> unfinishedItems)
        {
            FetchDownloadLink(unfinishedItems);
        }

        private async void FetchDownloadUrlsForMHR(string url)
        {
            if (currentWebSite == null)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "[E0]: 参数出错！");
                return;
            }
            //1.获取html以及初始化doc
            var doc = await FetchHtmlDocumentAsync(url);
            if (doc == null)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "未能获取到有效的下载链接。");
                return;
            }
            //2.获取连接
            //2.1
            //2.2 Step1 获取跳转链接
            List<DownloadItem> downloadItems = FetchIntermediateLink(doc, currentWebSite.ParseSelector.SelectorForTitle, currentWebSite.ParseSelector.SelectorForUrl1);
            if (downloadItems.Count == 0)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "未能获取到有效的下载链接。");
                return;
            }
            downloadItems.ForEach(item => downloadItemList.Add(item));
            //2.2 Step2 获取下载链接
            FetchDownloadLink(downloadItems);
        }

        /// <summary>
        /// 20240223 根据希望之声提供的明慧广播“天音净乐”2023错误格式做修正;
        /// 通常为：2023-01-03 【天音净乐】天音净乐第460集【最初的家园】 节目长度：15分3秒 <a href="/audio01/2023/1/3/tyjy_460_1503_32k.mp3" target="_blank">/audio01/2023/1/3/tyjy_460_1503_32k.mp3</a>
        /// 错误为：2023-04-04 【天音净乐】Thepageyouarelookingforistemporarilyunavailable.Pleasetryagainlater.<h3>天音净乐第463集【仓颉造字光明大显】</h3> 节目长度：15分10秒 <a href="/audio01/2023/4/4/tyjy_463_1510_32k.mp3" target="_blank">/audio01/2023/4/4/tyjy_463_1510_32k.mp3</a>
        /// </summary>
        /// <param name="url"></param>
        private async void FetchDownloadUrlsForMHRInSOH(string url)
        {
            if (currentWebSite == null)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "[E0]: 参数出错!");
                return;
            }
            //1.获取html以及初始化doc
            var doc = await FetchHtmlDocumentAsync(url, isMhrFromSoh: true);
            if (doc == null)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "未能获取到有效的下载链接。");
                return;
            }
            //2.获取连接
            //2.1
            //2.2 获取下载链接
            List<DownloadItem>? downloadItems = FetchDownloadLinkForMHRInSOH(doc, currentWebSite.ParseSelector.SelectorForTitle, currentWebSite.ParseSelector.SelectorForUrl1);
            if (downloadItems == null || downloadItems.Count == 0)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "未能获取到有效的下载链接。");
                return;
            }
            taskProgress.TaskProgress = 100;
            downloadItems.ForEach(item => downloadItemList.Add(item));
            CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false);
        }

        private bool CheckTbWebUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                MessageBoxError("“网页链接”为空！");
                return false;
            }
            if (!url.StartsWith("http", false, null))
            {
                MessageBoxError("“网页链接”格式错误！");
                return false;
            }
            return true;
        }

        private bool IsWebSiteSupported()
        {
            if (currentWebSite == null)
            {
                MessageBoxError("[E0]: 参数出错！");
                return false;
            }
            if (!WEB_SITE_NAMES_SUPPORTED.Contains(currentWebSite.WebSiteName))
            {
                MessageBoxError($"当前版本的程序不支持“{currentWebSite.WebSiteName}”！");
                return false;
            }
            return true;
        }

        private bool IsAllDownloadUrlFetched()
        {
            if (downloadItemList.Count > 0 && downloadItemList.All(item => !string.IsNullOrEmpty(item.downloadUrl)))
                return true;
            return false;
        }

        private bool IsAllFilesDownloaded()
        {
            if (downloadItemList.Count > 0 && downloadItemList.All(item => item.downloadResult == true))
                return true;
            return false;
        }

        /// <summary>
        /// 检查是否需要重新下载，或只是更新未获取下载链接的项（强制把剩余的获取完成，不考虑 选择性问题）
        /// 1.保持现状(有，且已经完成)，返回 null
        /// 2.要清空，这返回空列表
        /// 3.要继续未完成的，返回有效列表
        /// 【注意】选择和不选择，都必须把 downloadUrl 全部获取才能進入到下一步
        /// </summary>
        /// <returns></returns>
        private List<DownloadItem>? CheckDownloadUrlState(string url)
        {
            List<DownloadItem> items = new List<DownloadItem>();
            //1.20220619 检查是否是新的，新的连接或文件，则清空旧数据，就从新下载
            var isNewWebUrl = !string.IsNullOrEmpty(oldTaskTarget) && oldTaskTarget != url;
            if (isNewWebUrl)
            {
                oldTaskTarget = url;
                downloadItemList.Clear();
                return items;
            }
            //2.检查列表为空的状态
            if (downloadItemList.Count == 0)
                return items;
            //3.当前的连接或文件没有变化，检查其状态，决定下载情况
            bool isAllFetced = IsAllFilesDownloaded();//20240222 IsAllDownloadUrlFetched();
            if (isAllFetced)
            {
                if (MessageBoxQuestion("下载列表不为空，且已经成功获取下载链接。如果继续则会清空列表，从新获取下载列表。继续请按“确定”按钮"))
                {
                    return items;
                }
                else
                {
                    return null;
                }
            }
            //3.【20220612】 中间连接需要考虑，但是只要剔除了【 有标题 而 无中间连接 ==> 这样的情况几乎不存在】 的Item
            var unfinishedDownloadLinks = downloadItemList.Where(item => string.IsNullOrEmpty(item.downloadUrl)).ToList();
            if (unfinishedDownloadLinks.Any(item => string.IsNullOrEmpty(item.intermediateUrl)))
            {
                return items;//其中任一一项存在 intermediateUrl 无效的情况，就得重新获取
            }
            else
            {
                return unfinishedDownloadLinks;
            }
        }
        /// <summary>
        /// 1、null，表示取消此次任务，不執行任務；
        /// 2、不为空，则只下载未完成的。
        /// 与 CheckDownloadUrlState() 不同，这里只有两种情况
        /// 【注意】选择和不选择，都必须把前一步的 downloadUrl 全部获取才能進入到進入 【文件下載】階段
        /// 20240226 干净世界的下载不使用此函数
        /// </summary>
        /// <returns></returns>
        private List<DownloadItem>? CheckFileDownloadState()
        {
            //1.
            if (downloadItemList.Count == 0)
            {
                MessageBoxInformation("下载列表空，没有需要下载的文件！");
                return null;
            }
            if (downloadItemList.Any(item => string.IsNullOrEmpty(item.downloadUrl)))
            {
                MessageBoxInformation("下载列表中的“下载链接”中尚有未成功获取的，请先获取全部的下载链接后再执行下载任务。");
                return null;
            }
            //2.
            bool isAllDownloaded = IsAllFilesDownloaded();
            if (isAllDownloaded)
            {
                if (MessageBoxQuestion("已经成功下载所有文件，如果继续则会从新下载。继续请按“确定”按钮"))
                {
                    foreach (var item in downloadItemList)
                    {
                        item.downloadResult = false;
                    }
                    return downloadItemList.ToList();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                //3.【 列表被选择，且没有完成】 的Item
                return downloadItemList.Where(item => item.isSelected && !item.downloadResult).ToList();
            }
        }

        private List<DownloadItem> FetchIntermediateLink(HtmlDocument doc, string titleSelector, string urlSelector)
        {
            int id = 0;
            string intermediateUrl, fileName;
            return doc.QuerySelectorAll(urlSelector).ToList().ConvertAll(node =>
            {
                intermediateUrl = node.GetAttributeValue("href", "");
                fileName = titleSelector == "INNER_TEXT" ? node.InnerText : node.QuerySelector(titleSelector).InnerText;
                fileName = PatchTitle(fileName);
                return new DownloadItem(id++, PatchFileExtension(fileName), intermediateUrl, "", true);
            });
        }

        private List<DownloadItem>? FetchDownloadLinkForMHRInSOH(HtmlDocument doc, string titleSelector, string urlSelector)
        {
            int id = 0;
            string downloadUrl, fileName;
            var nodes = doc.QuerySelectorAll(urlSelector).ToList();
            if (nodes.Count != 3 || nodes[1].ChildNodes.Count == 0 || nodes[1].ChildNodes.Count % 3 != 0)
            {
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, "解析错误，网页代码格式发生变化。");
                return null;
            }
            var list = new List<DownloadItem>();
            var urlHistory = new HashSet<string>();//20241016 网页有重复的项目
            for (int i = 0; i < nodes[1].ChildNodes.Count; i += 3)
            {
                fileName = nodes[1].ChildNodes[i].InnerText;
                //20240223 根据希望之声提供的明慧广播“天音净乐”2023错误格式做修正;
                fileName = Regex.Replace(fileName, @"【天音净乐】[\w\W]*天音净乐第", "【天音净乐】天音净乐第");
                downloadUrl = nodes[1].ChildNodes[i + 1].GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(downloadUrl) || urlHistory.Contains(downloadUrl))
                    continue;
                urlHistory.Add(downloadUrl);
                fileName = PatchTitle(fileName);
                list.Add(new DownloadItem(id++, PatchFileExtension(fileName), "", downloadUrl, true));
            }
            return list;
        }

        private List<String> ParseDownloadUrlsByHtmlCodeWithTwoSteps(string htmlCode)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(htmlCode);
            return ParseDownloadUrlsFromDocumentWithTwoSteps(doc);
        }

        private List<String> ParseDownloadUrlsFromDocumentWithTwoSteps(HtmlDocument doc)
        {
            List<string> list = new List<string>();
            string selector = GetParseUrlSelector(true);
            if (string.IsNullOrEmpty(selector))
            {
                MessageBoxErrorWithoutResultOnUI("“下载链接”选择器是空的！");
                return list;
            }
            doc.QuerySelectorAll(selector).ToList().ForEach(node => list.Add(node.GetAttributeValue("href", "")));
            foreach (string url in list)
            {
                Log(url);
            }
            return list;
        }

        private string PatchFileExtension(string fileName)
        {
            if (currentWebSite == null)
            {
                MessageBoxError("[E0]: 参数出错！");
                return fileName;
            }
            if (fileName.ToLower().EndsWith(currentWebSite.ParseSelector.FileExtension.ToLower())) return fileName;
            return fileName + currentWebSite.ParseSelector.FileExtension;
        }

        private string GetParseUrlSelector(bool forDownloadUrl)//SelectorForUrl1 >>> intermediateUrl => SelectorForUrl2 >>> downloadUrl
        {
            if (currentWebSite == null)
            {
                return "";
            }
            if (currentWebSite.ParseSelector.ParseByTwoSteps)
            {
                return forDownloadUrl ? currentWebSite.ParseSelector.SelectorForUrl2 : currentWebSite.ParseSelector.SelectorForUrl1;
            }
            else
            {
                return currentWebSite.ParseSelector.SelectorForUrl1;
            }
        }

        private void CheckCheckedState()
        {
            int allCount = 0;
            foreach (DownloadItem item in downloadItemList)
            {
                if (item.isSelected) allCount++;
            }
            if (allCount == downloadItemList.Count)
            {
                LvDownloadItem.SelectAll();
                chkbSelectAll.IsChecked = true;
            }
        }

        private bool ValidateProxy()
        {
            bool useProxy = (bool)(ckbUseProxy.IsChecked ?? false);
            if (webSiteParseList != null)
                webSiteParseList.UseProxy = useProxy;
            proxyState.UseProxy = useProxy;
            if (!useProxy) return true;

            return ValidateTextBoxProxy();
        }

        private (string host, string port) ParseTbProxy()
        {
            string proxy = tbProxy.Text.Trim();
            var para = proxy.Split(':');
            if (para.Length != 2)
            {
                return ("", "");
            }
            return (para[0], para[1]);
        }

        private bool ValidateTextBoxProxy()
        {
            if (webSiteParseList == null) return false;

            var (sHost, sPort) = ParseTbProxy();
            if (!IsProxyHostValid(sHost))
            {
                MessageBoxError("代理的主机设置错误！");
                return false;
            }
            if (!IsProxyPortValid(sPort))
            {
                MessageBoxError("代理的端口设置错误！");
                return false;
            }

            int proxyPort = Int32.Parse(sPort);

            proxyState.IsProxyChanged = webSiteParseList.ProxyHost != sHost || webSiteParseList.ProxyPort != proxyPort;
            proxyState.ProxyHost = sHost;
            proxyState.ProxyPort = proxyPort;
            webSiteParseList.ProxyHost = sHost;
            webSiteParseList.ProxyPort = proxyPort;
            if (proxyState.IsProxyChanged)
            {
                SaveMainJson();
            }
            Log($"ValidateTextBoxProxy: useProxy = {proxyState.UseProxy}, isProxyChanged = {proxyState.IsProxyChanged}, host = {proxyState.ProxyHost}, port = {proxyState.ProxyPort}");
            return true;
        }

        private void CheckProxyStateOnCloseApp()
        {
            if (webSiteParseList == null) return;
            webSiteParseList.UseProxy = (bool)(ckbUseProxy.IsChecked ?? false);

            var (sHost, sPort) = ParseTbProxy();
            if (!IsProxyHostValid(sHost) || !IsProxyPortValid(sPort))
            {
                MessageBoxError("代理设置错误！不会保存最新的代理设置。");
                return;
            }
            int proxyPort = Int32.Parse(sPort);
            webSiteParseList.ProxyHost = sHost;
            webSiteParseList.ProxyPort = proxyPort;
        }

        private bool IsProxyHostValid(string host)
        {
            return !string.IsNullOrEmpty(host) && Regex.IsMatch(host, @"^([\w-]+\.)+[\w-]+(/[\w-./?%&=]*)?$");
        }

        private bool IsProxyPortValid(string portString)
        {
            if (string.IsNullOrEmpty(portString)) return false;
            if (!Regex.IsMatch(portString, @"\d+")) return false;
            int port = Int32.Parse(portString);
            return port >= 1024 && port <= 65535;
        }

        private bool IsProxyPortValid(int port)
        {
            return port >= 1024 && port <= 65535;
        }

        private void FetchDownloadLink(List<DownloadItem> list)
        {
            int threadNum = Int32.Parse(cmbThreadNum.Text);
            try
            {
                Log($"ThreadNum: {threadNum}");
                int maxWorkerThreads = 0, maxCompletionPortThreads = 0;
                int minWorkerThreads = 0, minCompletionPortThreads = 0;

                ThreadPool.GetMaxThreads(out maxWorkerThreads, out maxCompletionPortThreads);
                ThreadPool.GetMinThreads(out minWorkerThreads, out minCompletionPortThreads);
                ThreadPool.SetMaxThreads(threadNum, threadNum);
                ThreadPool.SetMinThreads(threadNum, threadNum);
                countCompleted = 0;
                System.Threading.ThreadPool.QueueUserWorkItem(w =>
                {
                    Parallel.For(0, list.Count, index =>
                    {
                        //1.获取
                        DownloadItem item = list[index];
                        string url = PatchFragmentUrl(item.intermediateUrl);
                        if (string.IsNullOrEmpty(url))//
                        {
                            Log($"ERROR: {item.id} {item.fileName}");
                            return;//!!!需要测试
                        }
                        //isTS 在前面取消了，这里不考虑
                        string targetUrl = test ? $"http://{item.id}" : FetchOneDownloadUrl(url, item.id);////
                        //2.成功或失败，都前行了一步
                        taskProgress.TaskProgress = ++countCompleted * 100 / list.Count;
                        //3.处理结果
                        if (!string.IsNullOrEmpty(targetUrl))
                        {
                            Log($"已经成功，结果：{targetUrl}");
                            this.Dispatcher.Invoke(() =>
                            {
                                downloadItemList[item.id].downloadUrl = PatchFragmentUrl(targetUrl);
                            });
                        }
                        else
                        {
                            Log($"ERROR: {item.id} {item.fileName}");
                        }
                    });
                    ThreadPool.SetMinThreads(maxWorkerThreads, maxCompletionPortThreads);
                    ThreadPool.SetMaxThreads(minWorkerThreads, minCompletionPortThreads);
                    taskProgress.TaskProgress = 100;//补偿下，有时太快。
                    CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false);
                });
            }
            catch (Exception e1)
            {
                Log("Error: " + e1.Message);
                CtrolWidgetsOnTask(AppTask.TASK_FETCH_DOWNLOAD_URL, false, e1.Message);
            }
        }

        private string PatchFragmentUrl(string fragmentUrl)
        {
            if (currentWebSite == null) return "";
            if (fragmentUrl.StartsWith("//"))
            {
                fragmentUrl = currentWebSite.WebSiteUrlScheme + fragmentUrl;
            }
            else if (fragmentUrl.StartsWith("/"))
            {
                fragmentUrl = currentWebSite.WebSiteUrl + fragmentUrl;
            }
            return fragmentUrl;
        }

        private string FetchOneDownloadUrl(string url, int index)
        {
            string targetUrl = "";
            Log($"FetchOneDownloadUrl: 获取{index} - {url}");
            try
            {
                string html = FetchHtml(url);
                var list = ParseDownloadUrlsByHtmlCodeWithTwoSteps(html);
                foreach (string url1 in list)
                {
                    if (!IsUrlMatchRule(url1))
                        continue;
                    targetUrl = url1;
                }
                return targetUrl;
            }
            catch (Exception e)
            {
                Log($"FetchOneDownloadUrl: 获取{index} - {url}出现异常，错误信息：{e.Message}");
            }
            return targetUrl;
        }

        private bool IsUrlMatchRule(string url)
        {
            if (currentWebSite == null) return false;
            if (!string.IsNullOrEmpty(currentWebSite.ParseSelector.FileNameKeywordExclude) && url.Contains(currentWebSite.ParseSelector.FileNameKeywordExclude))
                return false;
            if (!string.IsNullOrEmpty(currentWebSite.ParseSelector.FileNameKeywordInclude) && !url.Contains(currentWebSite.ParseSelector.FileNameKeywordInclude))
                return false;
            if (!string.IsNullOrEmpty(currentWebSite.ParseSelector.FileExtension) && !url.EndsWith(currentWebSite.ParseSelector.FileExtension))
                return false;
            return true;
        }

        #endregion

        /////////////////////////////////////////////////////
        ///7.下载 20220605
        #region   
        private void DownloadAll()
        {
            //0.
            errors.Clear();
            //1.检查网络状态
            if (!ValidateProxy()) return;
            //2.检查当前完成状态
            var list = CheckFileDownloadState();
            //2.1 [1]获取完成，不需要从新获取
            if (list == null) return;
            //2.2 [2] 20220622 修改代理
            Log($"DownloadAll: proxy has Changed ={proxyState.IsProxyChanged}");
            if (proxyState.IsProxyChanged)
            {
                list.ForEach(item => item.downloadService?.ResetProxy(CreateProxy()));
            }
            //2.3
            totalDownloadNum = list.Count;
            countCompleted = 0;
            CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, true);
            //4.2 [2]继续完成未完成的，包括全新或从新下载
            int threadNum = threadNums[cmbThreadNum.SelectedIndex];
            downloadList.Clear();
            downloadList.AddRange(list);
            List<KeyValuePair<int, DownloadItem>> downloadPairs = new List<KeyValuePair<int, DownloadItem>>();
            try
            {
                currentTaskId = threadNum - 1;
                int realNum = Math.Min(threadNum, downloadList.Count);
                for (int i = 0; i < realNum; i++)
                {
                    downloadPairs.Add(new KeyValuePair<int, DownloadItem>(i, downloadList[i]));
                }
                ParallelAction(downloadPairs);
            }
            catch (Exception e1)
            {
                Log("Error: " + e1.Message);
                CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false, e1.Message);
            }
        }

        private void ParallelAction(List<KeyValuePair<int, DownloadItem>> downloadPairs)
        {
            Parallel.For(0, downloadPairs.Count, async index =>
            {
                await DownloadFile(downloadPairs[index]).ConfigureAwait(false);
            });
        }

        private DownloadConfiguration CreateDownloadConfiguration()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
            var cookies = new CookieContainer();
            //cookies.Add(new Cookie("download-type", "test") { Domain = "domain.com" });

            RequestConfiguration requestConfiguration = new RequestConfiguration
            {
                // config and customize request headers
                Accept = "*/*",
                CookieContainer = null,//cookies,
                Headers = new WebHeaderCollection(), // { Add your custom headers }
                KeepAlive = true,
                ProtocolVersion = HttpVersion.Version11, // Default value is HTTP 1.1
                UseDefaultCredentials = false,
                UserAgent = userAgent,//"Mozilla/5.0 (Windows NT 10.0; Win64; x64)",/*USER_AGENT_DEFAULT,//*/
                //null,//"",//20220628取消 webSiteParseList.UserAgent,//"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:91.0) Gecko/20100101 Firefox/91.0",//$"DownloaderForMHR/{version}",
                Proxy = CreateProxy(),
            };
            int chunkCount = 1;
            return new DownloadConfiguration
            {
                // usually, hosts support max to 8000 bytes, default values is 8000
                BufferBlockSize = 10240,
                // file parts to download, default value is 1
                ChunkCount = chunkCount, //8,
                // download speed limited to 2MB/s, default values is zero or unlimited
                MaximumBytesPerSecond = 1024 * 1024 * 2,
                // the maximum number of times to fail
                MaxTryAgainOnFailover = 5,
                // release memory buffer after each 50 MB
                MaximumMemoryBufferBytes = 1024 * 1024 * 50,
                // download parts of file as parallel or not. Default value is false
                ParallelDownload = true,
                // number of parallel downloads. The default value is the same as the chunk count
                ParallelCount = 4,//?
                // timeout (millisecond) per stream block reader, default values is 1000
                Timeout = 1000,
                // set true if you want to download just a specific range of bytes of a large file
                RangeDownload = false,
                // floor offset of download range of a large file
                RangeLow = 0,
                // ceiling offset of download range of a large file
                RangeHigh = 0,
                // clear package chunks data when download completed with failure, default value is false
                //ClearPackageOnCompletionWithFailure = true,
                // minimum size of chunking to download a file in multiple parts, default value is 512
                MinimumSizeOfChunking = 1024,
                // Before starting the download, reserve the storage space of the file as file size, default value is false
                ReserveStorageSpaceBeforeStartingDownload = true,
                // config and customize request headers
                RequestConfiguration = requestConfiguration,
            };
        }

        private WebProxy CreateProxy()
        {
            Uri? uri = proxyState.UseProxy ? new Uri($"http://{proxyState.ProxyHost}:{proxyState.ProxyPort}") : null;
            return new WebProxy()
            {
                Address = uri,
                UseDefaultCredentials = false,
                Credentials = System.Net.CredentialCache.DefaultNetworkCredentials,
                BypassProxyOnLocal = true
            };
        }

        private async Task<DownloadService> DownloadFile(KeyValuePair<int, DownloadItem> downloadPair)
        {
            DownloadItem downloadItem = downloadPair.Value;
            DownloadService? downloadService = downloadItem.downloadService;
            if (downloadService != null && downloadService.Package.SaveProgress >= 0)
            {
                Log($"DownloadFile 继续下载 {downloadService.TaskId} -> {downloadPair.Key} : {downloadItem.fileName}...");
                downloadService.TaskId = downloadPair.Key;
                await downloadService.DownloadFileTaskAsync(downloadService.Package);
                return downloadService;
            }

            Log($"DownloadFile 开启新下载 {downloadPair.Key} : {downloadItem.fileName}...");
            downloadService = CreateDownloadService(downloadPair.Key, null);
            downloadItem.downloadService = downloadService;//TEST

            if (string.IsNullOrWhiteSpace(downloadItem.fullFileName))
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, new DirectoryInfo(downloadItem.folderPath)).ConfigureAwait(false);
            }
            else
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, downloadItem.fullFileName).ConfigureAwait(false);
            }

            return downloadService;
        }

        private DownloadService CreateDownloadService(int taskId, DownloadPackage? package)
        {
            DownloadService downloadService = new DownloadService(taskId, CreateDownloadConfiguration());
            downloadService.ChunkDownloadProgressChanged += OnChunkDownloadProgressChanged;
            downloadService.DownloadProgressChanged += OnDownloadProgressChanged;
            downloadService.DownloadFileCompleted += OnDownloadFileCompleted;
            downloadService.DownloadStarted += OnDownloadStarted;
            if (package != null)
            {
                downloadService.Package = package;
            }
            return downloadService;
        }

        private void OnDownloadStarted(object? sender, DownloadStartedEventArgs e)
        {
            Log($"OnDownloadStarted: TaskId = {e.TaskId}");
            if (e.TaskId < 0)
            {
                Log($"OnDownloadStarted ERROR: TaskId = {e.TaskId}");
                return;
            }

            //DownloadItem item = downloadList.ElementAt(e.TaskId);
            Log($"OnDownloadStarted [{e.TaskId} - {e.FileName}] 开始下载...");
        }

        private async void OnDownloadFileCompleted(object? sender, AsyncDownloadCompletedEventArgs e)
        {
            Log($"OnDownloadFileCompleted: TaskId = {e.TaskId}");
            if (webSiteParseList == null)
            {
                Log($"OnDownloadFileCompleted: TaskId = 意外错误！");
                return;
            }

            //1.
            DownloadItem item = downloadList.ElementAt(e.TaskId);

            if (e.Cancelled)
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载被取消！");
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
            }
            else if (e.Error != null)
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载出错，详情：{e.Error}。");
                errors.Add(e.Error.Message);
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
            }
            else
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载成功！");
                //成功后再清除，否则不能断点下载
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
                item.downloadResult = true;
                if (item.downloadService != null)
                    await item.downloadService.Clear();
            }
            if (countCompleted == totalDownloadNum)
            {
                CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false, string.Join("\r\n", errors));
                return;
            }
            //2.完成一个，开始下一个，因此，在开始的 ParallelAction 确定好数量后就会一直延续。
            if (!abStartDownload.Get())
            {
                CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false);
                return;
            }
            List<KeyValuePair<int, DownloadItem>>? downloadPairs = TakeOneTask();
            if (downloadPairs == null)
            {
                //没有下载任务可以执行，且所有的下载都成功
                if (downloadItemList.ToList().All(item1 => item1.downloadResult == true))
                {
                    CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false);
                }
                return;
            }
            Log($"OnDownloadFileCompleted >>> 开始新的下载： {downloadPairs[0].Key} - {downloadPairs[0].Value.fileName}！");
            ParallelAction(downloadPairs);
        }

        private List<KeyValuePair<int, DownloadItem>>? TakeOneTask()
        {
            lock (obj)
            {
                currentTaskId++;
                if (currentTaskId > downloadList.Count - 1)
                {
                    Log(">>> 没有更多需要下载的了...");
                    return null;
                }

                Log($"OnDownloadFileCompleted >>> 新的下载 index = {currentTaskId}！");
                List<KeyValuePair<int, DownloadItem>> downloadPairs = new List<KeyValuePair<int, DownloadItem>>();
                DownloadItem newItem = downloadList.ElementAt(currentTaskId);
                downloadPairs.Add(new KeyValuePair<int, DownloadItem>(currentTaskId, newItem));

                return downloadPairs;
            }
        }

        private void OnChunkDownloadProgressChanged(object? sender, Downloader.DownloadProgressChangedEventArgs e)
        {
            //DownloadItem item = downloadList[e.TaskId];
            //Log($"OnChunkDownloadProgressChanged [{e.TaskId}: ProgressId = {e.ProgressId}, ProgressPercentage = {e.ProgressPercentage}%");
        }

        private void OnDownloadProgressChanged(object? sender, Downloader.DownloadProgressChangedEventArgs e)
        {
            UpdateDwonloadInfo(e);
        }

        private void UpdateDwonloadInfo(Downloader.DownloadProgressChangedEventArgs e)
        {
            if (e.TaskId < 0)
            {
                Log($"OnDownloadProgressChanged -> UpdateTitleInfo ERROR: TaskId = {e.TaskId}");
                return;
            }
            DownloadItem item = downloadList.ElementAt(e.TaskId);

            double nonZeroSpeed = e.BytesPerSecondSpeed + 0.0001;
            int estimateTime = (int)((e.TotalBytesToReceive - e.ReceivedBytesSize) / nonZeroSpeed);
            //bool isMinutes = estimateTime >= 60;
            //string timeLeftUnit = "seconds";
            //if (isMinutes)
            //{
            //    estimateTime /= 60;
            //    //timeLeftUnit = "minutes";
            //}
            //
            //if (estimateTime < 0)
            //{
            //    estimateTime = 0;
            //    //timeLeftUnit = "unknown";
            //}

            string avgSpeed = CalcMemoryMensurableUnit(e.AverageBytesPerSecondSpeed);
            string speed = CalcMemoryMensurableUnit(e.BytesPerSecondSpeed);
            string bytesReceived = CalcMemoryMensurableUnit(e.ReceivedBytesSize);
            string totalBytesToReceive = CalcMemoryMensurableUnit(e.TotalBytesToReceive);
            string progressPercentage = $"{e.ProgressPercentage:F3}".Replace("/", ".");
            Log($"TaskID:{e.TaskId} => e.ProgressPercentage={e.ProgressPercentage}, totalBytesToReceive={totalBytesToReceive}");
            if (e.ProgressPercentage >= 100)
            {
                item.downloadResult = true;
                Log($"OnDownloadProgressChanged [{e.TaskId}] 下载完成");
            }

            item.downloadProgress = (int)e.ProgressPercentage;
            item.downloadSpeed = $"{speed}/s";//$"{speed}/s | {avgSpeed}/s";
            item.fileSize = $"{bytesReceived} / {totalBytesToReceive}";
        }
        #endregion

        /////////////////////////////////////////////////////
        ///8.控件控制
        #region

        private bool CheckParseSelectorsValidity()
        {
            if (currentWebSite == null)
            {
                EnableWidgesOnParseSelectorChanged(false);
                MessageBoxError("当前网站信息不存在！");
                return false;
            }
            if (currentWebSite.ParseSelector == null)
            {
                EnableWidgesOnParseSelectorChanged(false);
                MessageBoxError("当前网站解析器不存在！");
                return false;
            }
            if (!IsParseSelectorValid(currentWebSite.ParseSelector))
            {
                EnableWidgesOnParseSelectorChanged(false);
                MessageBoxError("当前网站没有有效的解析器！");
                return false;
            }
            EnableWidgesOnParseSelectorChanged(true);
            return true;
        }

        private bool IsParseSelectorValid(ParseSelector selector)
        {
            if (currentWebSite == null) return false;
            if (string.IsNullOrWhiteSpace(selector.SelectorName) || string.IsNullOrWhiteSpace(selector.SelectorForTitle)
                            || string.IsNullOrWhiteSpace(selector.SelectorForUrl1) || string.IsNullOrWhiteSpace(selector.FileExtension))
                return false;
            if (selector.ParseByTwoSteps && string.IsNullOrWhiteSpace(selector.SelectorForUrl2))
                return false;
            if (selector.ParseQuality && string.IsNullOrWhiteSpace(selector.SelectorForQuality))
                return false;
            return true;
        }
        private void InitThreadNumCombox(int threadNum)
        {
            int pos = threadNums.IndexOf(threadNum);
            int posDefault = threadNums.IndexOf(DEFAULT_THREAD_NUM);
            cmbThreadNum.SelectedIndex = pos == -1 ? posDefault : pos;
        }

        private void AdjustIndex(WebSiteParseList? webSiteParseList, bool forParse)
        {
            if (webSiteParseList == null)
                return;
            if (forParse)//解析
            {
                if (webSiteParseList.WebSites.Count == 0 || webSiteParseList.CurrentWebSiteIndex <= 0)
                {
                    webSiteParseList.CurrentWebSiteIndex = -1;
                }
                else if (webSiteParseList.CurrentWebSiteIndex >= 1 && webSiteParseList.CurrentWebSiteIndex <= webSiteParseList.WebSites.Count)
                {

                    webSiteParseList.CurrentWebSiteIndex--;
                }
            }
            else //保存
            {
                if (webSiteParseList.WebSites.Count == 0)
                {
                    webSiteParseList.CurrentWebSiteIndex = 0;
                }
                else if (webSiteParseList.CurrentWebSiteIndex >= 0)
                {
                    webSiteParseList.CurrentWebSiteIndex++;
                }
            }

        }

        private void InitSavedDownloadPackages(List<DownloadPackage> packages)
        {
            try
            {
                downloadItemList.Clear();
                if (packages == null || packages.Count == 0)
                {
                    Log("没有保存的下载信息！");
                    return;
                }
                int index = 0;
                string fileName, folderPath, directoryName;
                packages.ForEach(package =>
                {
                    fileName = Path.GetFileName(package.FileName);
                    directoryName = (Path.GetDirectoryName(package.FileName) ?? "").TrimEnd('\\');
                    folderPath = DOWNLOAD_PATH + (string.IsNullOrEmpty(directoryName) ? "" : $@"{directoryName}");
                    DownloadItem item = new DownloadItem(index, fileName, "", package.Urls[0], true, folderPath: folderPath, downloadResult: package.IsSaveComplete);
                    package.FileName = folderPath + "\\" + fileName;//20240228 GJSJ不应该添加，别的要添加，因为downloadPackage 继续下载需要
                    item.downloadService = CreateDownloadService(index, package);

                    string downloadSpeed = "0.0B/s | 0.0B/s";
                    string bytesReceived = CalcMemoryMensurableUnit(package.ReceivedBytesSize);
                    string totalBytesToReceive = CalcMemoryMensurableUnit(package.TotalFileSize);
                    var fileSize = $"{bytesReceived}/{totalBytesToReceive}";//20240227 ???

                    item.setDownloadInfo((int)package.SaveProgress, downloadSpeed, fileSize, package.IsSaveComplete);
                    Log($"InitSavedDownloadPackages: {index}-{item.fileName}");
                    downloadItemList.Add(item);

                    index++;
                });

            }
            catch (Exception ex)
            {
                Log($"InitSavedDownloadPackages 出现错误：{ex.Message}");
            }
        }

        #endregion

        /////////////////////////////////////////////////////
        ///9. 控件控制与消息显示
        #region
        /// <summary>
        /// startTask=false, msg 不空时，表示有错误发生。
        /// </summary>
        /// <param name="task"></param>
        /// <param name="startTask"></param>
        /// <param name="msg"></param>
        private void CtrolWidgetsOnTask(AppTask task, bool startTask, string errDetail = "")
        {
            countCompleted = 0;

            string taskInfo = "";
            string errInfo = "";
            bool noErrInfo = string.IsNullOrEmpty(errDetail);
            switch (task)
            {
                case AppTask.TASK_FETCH_DOWNLOAD_URL:
                    abFetchDownloadUrl.Set(startTask);
                    taskInfo = startTask ? "正在获取下载链接" : (noErrInfo ? "完成获取下载链接任务！" : "获取下载链接出错");
                    if (!noErrInfo) errInfo = $"获取下载链接出错！详情：{errDetail}";
                    break;
                case AppTask.TASK_DOWNLOAD:
                    abStartDownload.Set(startTask);
                    taskInfo = startTask ? "正在下载文件" : (noErrInfo ? "完成下载任务！" : "下载出错");
                    if (!noErrInfo) errInfo = $"下载出错！详情：{errDetail}";
                    break;
                case AppTask.TASK_MERGE_TS_FILES:
                    abMergeTSFiles.Set(startTask);
                    taskInfo = startTask ? "正在合并文件" : (noErrInfo ? "完成合并文件任务！" : "合并TS文件出错");
                    if (!noErrInfo) errInfo = $"合并TS文件出错！详情：{errDetail}";
                    break;
            }
            ShowTaskInfoOnUI(taskInfo);
            if (!noErrInfo) MessageBoxErrorWithoutResultOnUI(errInfo);
            EnableWidgesOnUI(task, !startTask);
            if (startTask)
                ResetProgressBar();
        }

        private void ShowTaskInfoOnUI(string msg)
        {
            this.Dispatcher.Invoke(() =>
            {
                lbTaskInfo.Content = msg;
            });
        }

        private void ResetProgressBar()
        {
            pbTaskInfo.Value = 0;
        }

        private void EnableWidgesOnUI(AppTask task, bool isEnabled)
        {
            this.Dispatcher.Invoke(() =>
            {
                cmbWebSite.IsEnabled = isEnabled;
                cmbSiteSection.IsEnabled = isEnabled;
                cmbSiteCategory.IsEnabled = isEnabled;
                cmbPageValue.IsEnabled = isEnabled;
                tbTaskTarget.IsEnabled = isEnabled;

                btnFetchDownloadLinks.IsEnabled = isEnabled;
                btnStartDownload.IsEnabled = isEnabled;
                btnStopDownload.IsEnabled = task == AppTask.TASK_DOWNLOAD ? true : isEnabled;

                ckbUseProxy.IsEnabled = isEnabled;
                tbProxy.IsEnabled = isEnabled;
                cmbThreadNum.IsEnabled = isEnabled;
            });
        }

        private void EnableWidgesOnParseSelectorChanged(bool isEnabled)
        {
            cmbSiteSection.IsEnabled = isEnabled;
            cmbSiteCategory.IsEnabled = isEnabled;
            cmbPageValue.IsEnabled = isEnabled;
            tbTaskTarget.IsEnabled = isEnabled;

            btnFetchDownloadLinks.IsEnabled = isEnabled;
            btnStartDownload.IsEnabled = isEnabled;
            btnStopDownload.IsEnabled = isEnabled;

            ckbUseProxy.IsEnabled = isEnabled;
            tbProxy.IsEnabled = isEnabled;
            cmbThreadNum.IsEnabled = isEnabled;
        }

        private void MessageBoxInformationWithoutResultOnUI(string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            });

        }

        private void MessageBoxErrorWithoutResultOnUI(string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private bool MessageBoxInformation(string message)
        {
            return MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK;
        }

        private bool MessageBoxError(string message)
        {
            return MessageBox.Show(this, message, "错误", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK;
        }

        private bool MessageBoxErrorWith2Btns(string message)
        {
            return MessageBox.Show(this, message, "错误", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK;
        }

        private bool MessageBoxQuestion(string message)
        {
            return MessageBox.Show(this, message, "询问", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        }

        private ProgressDialog ShowProgressDialog(string message, string title = "提示")
        {
            ProgressDialog dialog = new ProgressDialog();
            dialog.Owner = this;
            dialog.Title = title;
            dialog.lbTaskInfo.Content = message;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
            return dialog;
        }

        #endregion

        /////////////////////////////////////////////////////
        ///10. 一般控件事件【1】
        #region 
        string strPreviousProxyPort = "";
        bool bIsPasteOperation = false;

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = myWindows.Width - 12 - 870 - 30;
            col7.Width = width - 10;
            gvcDownloadProgress.Width = width;
        }

        private void tbDownloadUrl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                //MessageBoxInformation(((TextBlock)sender).Text);
                string m3u8Url = ((TextBlock)sender).Text;//20231123 直接赋值为index.m3u8了。 ==>.Replace("segment.ts", "index.m3u8");
                Clipboard.SetDataObject(m3u8Url);
                MessageBoxTimeoutA((IntPtr)0, "已将下载连接复制到剪贴板上了！", "提示", 0, 0, 3000);
                e.Handled = true;
            }
        }

        private void lbTaskInfo_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            //lbTaskInfo.Content = list[new Random().Next(3)];
        }

        private void tbProxyPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = new Regex("[^0-9]+").IsMatch(e.Text);
        }

        private void PasteNumericValidation(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(String)))
            {
                String input = (String)e.DataObject.GetData(typeof(String));
                if (new Regex("[0-9]+").IsMatch(input))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private async void cmbWebSite_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await InitCmbWebSite();
        }

        private void cmbSiteSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentWebSite == null || cmbSiteSection.SelectedIndex == -1) return;

            cmbSiteCategory.Items.Clear();
            currentWebSite.SiteSections[cmbSiteSection.SelectedIndex].SiteCategories.ForEach(item => cmbSiteCategory.Items.Add(item.CategoryName));
            cmbSiteCategory.SelectedIndex = GetCurrentSiteCategoryIndex();
        }

        private async void cmbSiteCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSiteCategory.SelectedIndex == -1 || currentWebSite == null) return;

            //oldCmbPageValueIndex = -1;//20220625
            cmbPageValue.Items.Clear();
            var siteCategory = currentWebSite.SiteSections[cmbSiteSection.SelectedIndex].SiteCategories[cmbSiteCategory.SelectedIndex];
            if (!siteCategory.needRefresh())
            {
                if (currentWebSite.WebSiteName == WEB_SITE_NAME_MHR)
                {
                    if (siteCategory.LowerLimit <= 0 || siteCategory.UpperLimit <= 0 || siteCategory.LowerLimit > siteCategory.UpperLimit)
                    {
                        MessageBoxError($"{siteCategory.CategoryName}的页序数据错误，请检查！");
                        return;
                    }
                    for (int i = siteCategory.LowerLimit; i <= siteCategory.UpperLimit; i++)
                    {
                        cmbPageValue.Items.Add(i.ToString());
                    }
                }
                else //if(currentWebSite.WebSiteName == WEB_SITE_NAME_MHR_IN_SOH)
                {
                    if (siteCategory.LimitRange == null || siteCategory.LimitRange.Count == 0)
                    {
                        MessageBoxError($"{siteCategory.CategoryName}的年份数据错误，请检查！");
                        return;
                    }
                    siteCategory.LimitRange.ForEach(item => cmbPageValue.Items.Add(item));
                }
                cmbPageValue.SelectedIndex = GetCurrentPageValueIndex();
                return;
            }
            if (currentWebSite.WebSiteName != WEB_SITE_NAME_MHR) return;
            while (!await RefreshPageValueForMultiplePage(siteCategory))
            {
                if (!MessageBoxErrorWith2Btns($"更新“{siteCategory.CategoryName}”的页序信息出错！请检查网络状态。如需再次尝试获取，请选择“确认”按钮。"))
                    return;
            }
        }

        private void cmbPageValue_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPageValue.SelectedIndex == -1 || currentWebSite == null)
            {
                tbTaskTarget.Text = "";
                return;
            }
            var siteCategory = currentWebSite.SiteSections[cmbSiteSection.SelectedIndex].SiteCategories[cmbSiteCategory.SelectedIndex];
            var id = siteCategory.CategoryId;
            string url = currentWebSite.ParseUrlModel.Replace("{1}", id).Replace("{2}", cmbPageValue.SelectedItem.ToString());
            tbTaskTarget.Text = url;

            InitWebSiteDownloadHistory();
        }

        private void chkbSelectAll_Click(object sender, RoutedEventArgs e)
        {
            //1.
            if (abFetchDownloadUrl.Get() || abStartDownload.Get() || abMergeTSFiles.Get())
            {
                chkbSelectAll.IsChecked = !chkbSelectAll.IsChecked;
                return;
            }
            //2.
            bool selectAll = ((CheckBox)sender).IsChecked == true;
            foreach (DownloadItem item in downloadItemList)
            {
                item.isSelected = selectAll;
            }
            if (selectAll)
                LvDownloadItem.SelectAll();
            else
                LvDownloadItem.SelectedItems.Clear();
        }

        private void chkbSelectItem_Click(object sender, RoutedEventArgs e)
        {
            //1.
            if (abFetchDownloadUrl.Get() || abStartDownload.Get() || abMergeTSFiles.Get())
            {
                var checkbox = sender as CheckBox;
                if (checkbox != null)
                    checkbox.IsChecked = !checkbox.IsChecked;
                return;
            }
            //2.
            bool select = ((CheckBox)sender).IsChecked == true;
            int index = (int)((CheckBox)sender).Tag;
            downloadItemList[index].isSelected = select;
            if (!select)
            {
                LvDownloadItem.SelectedItems.Remove(downloadItemList[index]);
                //LvDownloadItem.SelectedItems.Clear();
                //chkbSelectAll.IsChecked = false;
            }
            else
            {
                LvDownloadItem.SelectedItems.Add(downloadItemList[index]);
                //CheckCheckedState();
            }
            CheckCheckedState();
        }

        private void LvDownloadItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentWebSite == null) return;
            Log("LvDownloadItem_SelectionChanged...");
            List<int> listSelectedIndex = new List<int>();
            //1.找到选择了的项的id
            foreach (DownloadItem item in LvDownloadItem.SelectedItems)
            {
                listSelectedIndex.Add(item.id);
            }
            //2.重新勾选
            for (int i = 0; i < downloadItemList.Count; i++)
            {
                downloadItemList[i].isSelected = listSelectedIndex.Contains(i);
            }
            //3.判断是否全选
            chkbSelectAll.IsChecked = listSelectedIndex.Count == downloadItemList.Count;
        }

        private void cmbThreadNum_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (webSiteParseList == null) return;
            webSiteParseList.ThreadNum = threadNums[cmbThreadNum.SelectedIndex];
        }

        private void btnLvItemPause_Click(object sender, RoutedEventArgs e)
        {
            int index = (int)((Button)sender).Tag;
            Log($"btnLvItemPause_Click: index={index}");
            //downloadItemList[index].isSelected = select;
        }

        private void ckbUseProxy_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = ckbUseProxy.IsChecked ?? false;
            bdProxySelection.BorderBrush = isChecked ? null : Brushes.Red;
        }

        private async void myWindows_KeyDown(object sender, KeyEventArgs e)
        {
            Log($"{e.Key} pressed!");
            if(e.Key != Key.F5) return;

            if (currentWebSite == null) return;
            if (currentWebSite.SiteSections == null || currentWebSite.SiteSections.Count == 0 ||
               currentWebSite.SiteSections[0].SiteCategories == null || currentWebSite.SiteSections[0].SiteCategories.Count == 0 ||
               currentWebSite.SiteSections[0].SiteCategories.Any(item => item.needRefresh()))
            {
                await InitCmbWebSite();
            }
        }
        #endregion

        /////////////////////////////////////////////////////
        ///11. 任务控件事件【2】
        #region
        private void btnFetchDownloadLinks_Click(object sender, RoutedEventArgs e)
        {
            if (downloadItemList.Count > 0 && downloadItemList.Any(item => item.downloadService != null))
            {
                if (MessageBoxQuestion("下载列表不为空且有尚未完成的下载任务，若继续，则会取消未完成的下载任务并清空下载列表。继续，请按确认按钮；取消，请按取消按钮。"))
                {
                    downloadItemList.Clear();
                    RemoveCurrentWebSiteDownloadHistoryAndSave();
                }
                else
                {
                    return;
                }
            }
            FecthDownloadUrls();
        }

        private void btnStartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (currentWebSite == null)
            {
                MessageBoxError("[E0]: 参数出错！");
                return;
            }
            DownloadAll();
        }

        private void btnStopDownload_Click(object sender, RoutedEventArgs e)
        {
            if (currentWebSite == null)
            {
                MessageBoxError("[E0]: 参数出错！");
                return;
            }

            abStartDownload.Set(false);
            ShowTaskInfoOnUI("下载任务被取消");
            EnableWidgesOnUI(AppTask.TASK_DOWNLOAD, true);
            downloadItemList.ToList().ForEach(item =>
            {
                //if(item.downloadService != null && !item.downloadResult )
                item.downloadService?.CancelAsync();
            });
        }

        private void btnMergeTsFiles_Click(object sender, RoutedEventArgs e)
        {
            //1.检查是否下载列表是否为空
            //注意，下载TS，保存要根据序号保存如：0001.ts
            if (downloadItemList.Count == 0)
            {
                MessageBoxInformation("下载列表为空！");
                return;
            }
            //2.检查是否所有下载都完成
            if (downloadItemList.Count > 0 && downloadItemList.Any(item => item.downloadService != null))
            {
                MessageBoxInformation("下载列表不为空且有尚未完成的下载任务！");
                return;
            }
            //3.检查文件目录TS文件是否齐全，根据 tsTotalNum 来确定总数以及文件名对应文件是否存在

            //4.检查 ffmpg.exe 是否存在

            //5.合并文件

        }

        #endregion


        /////////////////////////////////////////////////////
        ///12. 测试 
        #region
        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
        }

        #endregion

    }
}

