﻿using BilibiliDM_PluginFramework;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Deployment.Application;
using System.Collections.Generic;

namespace DMPlugin_FollowAlert
{
    public class FollowAlertMain : DMPlugin
    {
        private const string PluginVersion = "1.1.0";

        internal static readonly string configPath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments), "弹幕姬", "Plugins", "关注提醒");
        internal static readonly string outputPath = Path.Combine(configPath, "输出.txt");
        internal static readonly string tplPath = Path.Combine(configPath,"模板.cfg"); //cfg后缀用于防止用户直接编辑内容或添加错文件

        private FollowAlertWindow w;
        private Thread t;

        private int errorCount = 0;
        private int masterId = 0;
        private int lastFollowId = 0;
        private List<string> unames = new List<string>();

        public FollowAlertMain()
        {
            this.PluginName = "关注提醒";
            this.PluginAuth = "宅急送队长";
            this.PluginDesc = "有新观众关注时发出提醒";
            this.PluginVer = PluginVersion;
            this.PluginCont = "https://f.danmuji.cn";

            w = new FollowAlertWindow(this);

            t = new Thread(loop) { Name = "FollowAlertRequestThread", IsBackground = true };
            t.Start();

            this.Connected += Main_Connected;
            this.Disconnected += Main_Disconnected;
        }

        private void Main_Connected(object sender, ConnectedEvtArgs e)
        {
            try
            {
                var json = httpGet("https://api.live.bilibili.com/live/getInfo?roomid=" + e.roomid);
                JObject j = JObject.Parse(json);
                masterId = j["data"]["MASTERID"].ToObject<int>();
                lastFollowId = 0;
                unames.Clear();
            }
            catch(Exception ex)
            {
                masterId = 0;
                AddDM("获取主播信息错误，请查看日志");
                Log("获取主播信息错误，此插件将不会工作，请重新连接直播间。\r\n错误信息：" + ex.Message);
            }
        }

        private void Main_Disconnected(object sender, DisconnectEvtArgs e)
        {
            masterId = 0;
            lastFollowId = 0;
            unames.Clear();
        }

        public override void DeInit()
        {
            w.processThread.Abort();
            File.WriteAllText(tplPath, w.outputTpl);
            File.WriteAllText(outputPath, string.Empty);
        }

        public override void Admin()
        {
            w.Show();
            w.Topmost = true;
            w.Focus();
            w.Topmost = false;
        }

        private void loop()
        {
            while(true)
            {
                Thread.Sleep(1000);// 延迟一秒
                if(Status && masterId != 0)
                {
                    JObject j;
                    try
                    {
                        j = JObject.Parse(httpGet("https://space.bilibili.com/ajax/friend/GetFansList?page=1&mid=" + masterId));
                        errorCount = 0;
                    }
                    catch(Exception ex)
                    {// 出现了什么错误
                        errorCount++;
                        if(errorCount >= 3)
                        {
                            base.Stop();
                            AddDM("插件错误，请查看日志");
                            Log("连续三次出现网络错误，已自动禁用插件，错误信息：" + ex.Message);
                        }
                        continue;//跳过此次处理的剩余代码
                    }
                    // 网络&Json解析完毕
                    try
                    {
                        if(j["status"].ToObject<bool>() && (j["data"]["list"] as JArray).Count > 0)
                        {// 如果返回的数据没有问题
                            if(lastFollowId == 0)
                            {// 如果这是第一次获取数据 保存最后一个关注的id
                                lastFollowId = j["data"]["list"][0]["record_id"].ToObject<int>();
                            }
                            else// 不是第一次获取数据
                            {// 比较是否有新关注
                                if(j["data"]["list"][0]["record_id"].ToObject<int>() > lastFollowId)
                                {// 如果有更新的关注的话
                                    foreach(JObject item in j["data"]["list"] as JArray)
                                    {// 循环列表
                                        if(item["record_id"].ToObject<int>() > lastFollowId && !unames.Contains(item["uname"].ToString()))
                                        {// 如果 record_id 更新，并且不是最近关注过的人
                                            display(item["uname"].ToString());

                                            // 防关注刷屏措施
                                            unames.Add(item["uname"].ToString());
                                            if(unames.Count > 9)
                                            { unames.RemoveAt(0); }
                                        }
                                    }// end of foreach
                                    // 重新设定最后一个关注id
                                    lastFollowId = j["data"]["list"][0]["record_id"].ToObject<int>();
                                }// 更新的关注if结束
                            }// 是否第一次获取if结束
                        }
                    }
                    catch(Exception ex)
                    {// 处理数据时错误，怀疑B站修改了返回数据格式
                        base.Stop();
                        AddDM("数据处理错误，请查看日志");
                        Log("在处理数据时发生错误，已自动禁用插件。请检查插件是否有新版本。\r\n错误信息：" + ex.ToString());
                        continue;
                    }
                }
            }
        }

        private void display(string name)
        {
            w.addName(name);
            fakeDM("新粉丝", name);
            Log("新粉丝：" + name);
        }

        private static string httpGet(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 10 * 1000; // 10秒
            request.Method = "GET";
            var response = (HttpWebResponse)request.GetResponse();
            var responseString = new StreamReader(response.GetResponseStream(), Encoding.UTF8).ReadToEnd();
            return responseString;
        }

        private static string UserAgent = getUserAgent();
        private static string getUserAgent()
        {
            string MainVersion;
            if(ApplicationDeployment.IsNetworkDeployed)
            { MainVersion = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString(); }
            else
            { MainVersion = "DebugMode"; }

            string ua = $@"Mozilla/5.0 (Bililive_dm {MainVersion}) AppleWebKit/537.36 (KHTML, like Gecko)"
                + $@" Chrome/56.0.2512.23 Safari/537.36 (FollowAlert/{PluginVersion}; DMPlugin)";
            return ua;
        }


        internal void fakeDM(string name, string text, bool red = true, bool fullscreen = false)
        {
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                try
                {// 以防万一
                    dynamic mw = System.Windows.Application.Current.MainWindow;
                    mw.AddDMText(name, text, red, fullscreen);
                }
                catch(Exception)
                {
                    this.AddDM(name + " " + text, fullscreen);
                }
            }));
        }
    }
}