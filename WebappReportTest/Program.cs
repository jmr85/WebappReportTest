using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Drawing;
using System.Drawing.Imaging;
using log4net;
using log4net.Config;

[assembly: XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]

namespace SeleniumReportTest
{
    class Program
    {
        static ChromeDriver driver;
        static TestConfig config;
        static ResumeLog resumeLog;
        static ILog logger;

        static void Main(string[] args)
        {
            if (Initialize())
            {
                logger.Info("Report Test LAUNCHED");

                var service = ChromeDriverService.CreateDefaultService(config.DriverPath);
                service.EnableVerboseLogging = config.EnableVerboseLogging;
                ChromeOptions chromeOptions = new ChromeOptions();
                chromeOptions.AcceptInsecureCertificates = true;
                chromeOptions.AddArgument("--ignore-certificate-errors");

                driver = new ChromeDriver(service, chromeOptions);

                //Driver de espera con maximo de 2 minutos por la carga de reportes
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

                resumeLog.LoadTotalUser(config.Users);

                foreach (sfNetUser user in config.Users)
                {
                    logger.Info(String.Format("BEGIN USER: {0}", user.Name));

                    logger.Info(String.Format("Login user {0}", user.Name));

                    if (doLogin(user.UserName, user.Password, wait))
                    {
                        string userFolder = config.OutputPath + "\\" + config.RunInstance + "\\" + user.Code.ToString() + " - " + user.Name;

                        if (!System.IO.Directory.Exists(userFolder))
                        {
                            System.IO.Directory.CreateDirectory(userFolder);
                        }

                        //Obtengo reportGroups

                        List<ReportGroup> reportGroups = new List<ReportGroup>();

                        //obtengo todos los menues de report groups
                        logger.Info("Waiting for report groups menu");

                        try
                        {
                            wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.CssSelector("[id^=menu_reportgroups]")));
                        }
                        catch
                        {
                            logger.Fatal("Elements with [id^=menu_reportgroups] didn't find them");
                        }

                        var menu_reportgroups = driver.FindElements(By.CssSelector("[id^=menu_reportgroups]"));
                        logger.Info("report groups menu finded");

                        foreach (var reportGroup in menu_reportgroups)
                        {
                            ReportGroup rg = new ReportGroup();

                            rg.Name = reportGroup.Text.Trim();
                            rg.Url = reportGroup.GetAttribute("href");

                            reportGroups.Add(rg);
                        }

                        logger.Info("Browsing report groups");

                        foreach (ReportGroup rg in reportGroups)
                        {
                            logger.Info(String.Format("{0} - {1}", rg.Name, rg.Url));

                            logger.Info(String.Format("Navigate to {0}", rg.Url));
                            driver.Navigate().GoToUrl(rg.Url);

                            //TODO: aca buscar que el elemento tenga texto
                            logger.Info("Waiting for page title");
                            wait.Until(driver => driver.FindElement(By.ClassName("page-title")).Text != String.Empty);

                            var pagetitle = driver.FindElement(By.ClassName("page-title"));

                            if (pagetitle != null)
                            {
                                rg.Name = pagetitle.Text.Trim();
                            }

                            string groupFolder = userFolder + "\\" + rg.Name;

                            if (!System.IO.Directory.Exists(groupFolder))
                            {
                                System.IO.Directory.CreateDirectory(groupFolder);
                            }

                            logger.Info("Waiting for report group is loaded");
                            try
                            {
                                wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementExists(By.CssSelector("[id^=report_group_is_loaded]")));
                            }
                            catch
                            {
                                logger.Error(String.Format("Group {0} is not loaded", rg.Name));
                            }

                            logger.Info("Get screenshot of report group");
                            GetScreenshot((string.Format("{0}\\_REPORTGROUP_{1}", groupFolder, rg.Name)), 60);

                            var reportelements = driver.FindElements(By.CssSelector("[id^=report_id]"));

                            foreach (var reportElement in reportelements)
                            {
                                Report report = new Report();

                                string elmId = reportElement.GetAttribute("id");
                                report.Code = int.Parse(elmId.Replace("report_id_", ""));
                                report.Name = reportElement.Text.Trim().Replace("&nbsp;", "");
                                rg.Reports.Add(report);
                            }

                            resumeLog.AddToTotalReports(rg.Reports);
                        }

                        //recorro los grupos

                        logger.Info("Browsing reports");

                        foreach (ReportGroup rg in reportGroups)
                        {
                            if (rg.Reports != null && rg.Reports.Count > 0)
                            {
                                string groupFolder = userFolder + "\\" + rg.Name;

                                foreach (Report report in rg.Reports)
                                {
                                    logger.Info(String.Format("{0} - {1}", report.Code, report.Name));

                                    driver.Navigate().GoToUrl(config.BaseUrl + "#/report/view/" + report.Code.ToString());

                                    logger.Info("Waiting for embedContainer");

                                    try
                                    {
                                        wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.Id("embedContainer")));
                                        logger.Info("embedContainer is loaded");

                                        logger.Info("Waiting for report is loaded");
                                        wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementExists(By.CssSelector("[id^=report_is_loaded]")));


                                        bool loadedOk;
                                        try
                                        {
                                            driver.FindElement(By.Id("report_is_loaded_with_error"));
                                            resumeLog.ReportFail(user.Name, rg.Name, report.Name);
                                            loadedOk = false;
                                        }
                                        catch
                                        {
                                            loadedOk = true;
                                            resumeLog.ReportOk();
                                        }

                                        if (loadedOk)
                                        {
                                            logger.Info("The report was loaded successfully");
                                        }
                                        else
                                        {
                                            logger.Error("There was a problem loading the report");
                                        }

                                        logger.Info("Get screenshot of report");
                                        GetScreenshot((string.Format("{0}\\{1}", groupFolder, report.Name)), 60);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Fatal(String.Format("Exception: {0}", ex));
                                    }


                                }
                            }
                        }

                        //deslogueo el user para continuar con el siguiente
                        logger.Info(String.Format("Logout user {0}", user.Name));
                        doLogOut(wait);

                        logger.Info(String.Format("END USER: {0}", user.Name));
                    }
                }
            }
            else
            {
                logger.Error("ERROR WHEN INITIALIZING");
            }

            logger.Info("Report Test FINISHED");

            logger.Info("----------------------------------");
            logger.Info(String.Format("Users detected: {0}", resumeLog.TotalUsers));
            logger.Info(String.Format("Users logged in OK: {0}", resumeLog.UsersLoginOk));
            logger.Info(String.Format("Users logged in with ERROR: {0}", resumeLog.UsersLoginFail.Count));

            foreach ( string user in resumeLog.UsersLoginFail )
            {
                logger.Info(String.Format("     {0}", user));
            }

            logger.Info(String.Format("Reports detected: {0}", resumeLog.TotalReports));
            logger.Info(String.Format("Reports OK: {0}", resumeLog.ReportsOk));
            logger.Info(String.Format("Reports with Error: {0}", resumeLog.ReportsFail.Count));

            foreach (ReportError reportFail in resumeLog.ReportsFail)
            {
                logger.Info(String.Format("     {0} | {1} | {2}", reportFail.User, reportFail.ReportGroup, reportFail.ReportName));
            }

            driver.Quit();
        }

        private static bool doLogin(string userName, string password, WebDriverWait wait)
        {
            bool ret = false;

            try
            {
                driver.Navigate().GoToUrl(config.BaseUrl + "#/login");

                wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.Id("txtUserName")));
                var txtUserName = driver.FindElement(By.Id("txtUserName"));
                wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.Id("txtUserPassword")));
                var txtUserPassword = driver.FindElement(By.Id("txtUserPassword"));
                wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.Id("btnLogin")));
                var btnLogin = driver.FindElement(By.Id("btnLogin"));

                logger.Info("Login Page is loaded");

                txtUserName.SendKeys(userName);
                txtUserPassword.SendKeys(password);
                btnLogin.Click();

                logger.Info("Execute login");

                try
                {
                    new WebDriverWait(driver, TimeSpan.FromSeconds(5)).Until(SeleniumExtras.WaitHelpers.ExpectedConditions.InvisibilityOfElementLocated(By.Id("btnLogin")));
                    ret = true;
                    logger.Info("Login Ok");
                    resumeLog.LoginOk();
                }
                catch
                {
                    ret = false;
                    logger.Error("Couldn't login user");
                    resumeLog.LoginFail(userName);
                }
            }
            catch
            {
                ret = false;
                logger.Error("There is a problem loading login page");
                resumeLog.LoginFail(userName);
            }

            return ret;
        }
        private static void doLogOut(WebDriverWait wait)
        {
            //ISessionStorage sessionStorage = ((IHasWebStorage)driver).WebStorage.SessionStorage;
            //ILocalStorage webStorage = ((IHasWebStorage)driver).WebStorage.LocalStorage;

            var btnLogout = driver.FindElement(By.Id("btnLogout"));

            IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
            js.ExecuteScript("arguments[0].click();", btnLogout);

            //no se puede hacer click xq es un elemento que no esta visible
            //btnLogout.Click();

            logger.Info("Logout");

            wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementIsVisible(By.Id("btnLogin")));

            //sessionStorage.Clear();
            //webStorage.Clear();
        }

        public static void GetScreenshot(string path, int quality)
        {

            string pathName = path + "_hq";
            string pathHq = pathName + ".Jpeg";
            string pathLQ = path + ".Jpeg";

            //Saco el screenshot
            Screenshot ss = ((ITakesScreenshot)driver).GetScreenshot();
            logger.Info("Take screenshot");

            ss.SaveAsFile(pathHq, ScreenshotImageFormat.Jpeg);
            logger.Info("Screenshot saved");

            Image newImg = Image.FromFile(pathHq);
            logger.Info("Open original screenshot");

            //Guardo el screenshot con la nueva calidad y borro el original
            SaveJpeg(pathLQ, newImg, quality);
            logger.Info("Save screenshot in lower quanlity");

            newImg.Dispose();
            logger.Info("Original screenshot dispose");

            File.Delete(pathHq);
            logger.Info("Original screenshot deleted");
        }

        public static void SaveJpeg(string path, Image img, int quality)
        {
            if (quality < 0 || quality > 100)
            {
                throw new ArgumentOutOfRangeException("Quality must be between 0 and 100.");
            }

            // Parametro del encoder para determinar la calidad 
            EncoderParameter qualityParam = new EncoderParameter(Encoder.Quality, quality);
            // Obtengo el codec para Jpeg
            ImageCodecInfo jpegCodec = GetEncoderInfo("image/jpeg");
            EncoderParameters encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = qualityParam;

            img.Save(path, jpegCodec, encoderParams);
        }

        private static ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            //Obtengo los codecs para todos los formatos de imagen
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();

            //Encuentro el codec correcto para ese mimeType 
            for (int i = 0; i < codecs.Length; i++)
                if (codecs[i].MimeType == mimeType)
                    return codecs[i];

            return null;
        }

        static bool Initialize()
        {
            bool ret = false;

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json", optional: false);

            IConfiguration configuration = builder.Build();

            var testConfig = configuration.GetSection("TestConfig").Get<TestConfig>();

            if (string.IsNullOrEmpty(testConfig.DriverPath))
            {
                testConfig.DriverPath = System.AppContext.BaseDirectory + @"\Dependencies\chromedriver_win32";
            }

            if (string.IsNullOrEmpty(testConfig.OutputPath))
            {
                testConfig.OutputPath = System.AppContext.BaseDirectory + @"\Output";
            }

            config = testConfig;

            resumeLog = new ResumeLog();

            GlobalContext.Properties["LogFolderName"] = config.RunInstance;

            logger = LogManager.GetLogger("RollingFile");

            ret = true;

            return ret;
        }
    }


    public class TestConfig
    {
        public string BaseUrl { get; set; }

        public string OutputPath { get; set; }

        public string DriverPath { get; set; }

        public bool EnableVerboseLogging { get; set; }

        public int ImplicitWaitSeconds { get; set; }

        public String RunInstance { get; set; }

        public List<sfNetUser> Users { get; set; }

        public TestConfig()
        {
            Users = new List<sfNetUser>();

            RunInstance = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");

            EnableVerboseLogging = false;

            ImplicitWaitSeconds = 10;
        }

    }

    public class sfNetUser
    {
        public int Code { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        public sfNetUser()
        {

        }
    }

    public class ReportGroup
    {
        public int Code { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }

        public List<Report> Reports { get; set; }

        public ReportGroup()
        {
            Reports = new List<Report>();
        }
    }

    public class Report
    {
        public int Code { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }
    }

    public class ResumeLog
    {
        public int TotalUsers { get; set; }
        public int UsersLoginOk { get; set; }
        public List<string> UsersLoginFail { get; set; }

        public int TotalReports { get; set; }
        public int ReportsOk { get; set; }

        public List<ReportError> ReportsFail { get; set; }

        public ResumeLog()
        {
            TotalUsers = 0;
            UsersLoginOk = 0;
            UsersLoginFail = new List<string>();
            TotalReports = 0;
            ReportsOk = 0;
            ReportsFail = new List<ReportError>();
        }

        public void LoadTotalUser(List<sfNetUser> users)
        {
            this.TotalUsers = users.Count;
        }

        public void LoginOk()
        {
            this.UsersLoginOk++;
        }

        public void LoginFail(string user)
        {
            this.UsersLoginFail.Add(user);
        }

        public void AddToTotalReports(List<Report> reports)
        {
            this.TotalReports += reports.Count;
        }

        public void ReportOk()
        {
            this.ReportsOk++;
        }

        public void ReportFail(string userName, string reportGroup, string reportName)
        {
            ReportError reportError = new ReportError()
            {
                User = userName,
                ReportGroup = reportGroup,
                ReportName = reportName,
                //Error = error
            };

            ReportsFail.Add(reportError);
        }
    }

    public class ReportError
    {
        public string User { get; set; }
        public string ReportGroup { get; set; }
        public string ReportName { get; set; }
        public string Error {  get; set; }
    }
}
