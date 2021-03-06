﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Runtime;
using System.Threading.Tasks;

namespace TCPCS
{
    /// <summary>
    /// 服务端
    /// </summary>
    public class Server
    {
        #region 变量
        #region Tcp连接套接字储存字典
        // Tcp连接套接字储存字典结构示意图：
        // SocketIPEndPointDict(Dictionary)
        // ----IP:Port(IPEndPoint)
        // ----SocketIdDict(Dictionary)
        //     ----SocketID(int)
        //     ----Sockrt(TcpSocket)
        /// <summary>
        /// 套接字储存字典
        /// </summary>
        public Dictionary<long, Socket> SocketIdDict = new Dictionary<long, Socket>();
        /// <summary>
        /// 以网络终结点为key的套接字储存字典的储存字典
        /// </summary>
        public Dictionary<IPEndPoint, Dictionary<long, Socket>> SocketIPEndPointDict = new Dictionary<IPEndPoint, Dictionary<long, Socket>>();
        #endregion
        #region 委托
        /// <summary>
        /// 客户端消息事件委托
        /// </summary>
        /// <param name="ListenIPEndPoint">连接的网络终结点</param>
        /// <param name="SocketDictID">套接字ID</param>
        /// <param name="MessagesArray">消息内容数组</param>
        /// <param name="MessagesLength">消息的长度</param>
        public delegate void ClientMessagesDelegate(IPEndPoint ListenIPEndPoint, long SocketDictID, byte[] MessagesArray, int MessagesLength);
        /// <summary>
        /// 客户端消息事件委托
        /// </summary>
        public ClientMessagesDelegate ClientMessages;
        /// <summary>
        /// Tcp套接字终止连接事件委托
        /// </summary>
        /// <param name="ListenIPEndPoint">连接的网络终结点</param>
        /// <param name="SocketDictID">套接字ID</param>
        public delegate void ConnentStopDelegate(IPEndPoint ListenIPEndPoint, long SocketDictID);
        /// <summary>
        /// Tcp套接字终止连接事件委托
        /// </summary>
        public ConnentStopDelegate ConnentStop;
        /// <summary>
        /// Tcp客户端连接事件委托
        /// </summary>
        /// <param name="ListenIPEndPoint"></param>
        /// <param name="SocketDictID"></param>
        public delegate void ClientConnentDelegate(IPEndPoint ListenIPEndPoint, long SocketDictID);
        /// <summary>
        /// Tcp客户端连接事件委托
        /// </summary>
        public ClientConnentDelegate ClientConnent;
        #endregion
        #region 字典
        /// <summary>
        /// 侦听线程
        /// </summary>
        Dictionary<IPEndPoint, Thread> ListenThreadDict = new Dictionary<IPEndPoint, Thread>();
        /// <summary>
        /// 端口侦听套接字
        /// </summary>
        Dictionary<IPEndPoint, Socket> ListenSocketDict = new Dictionary<IPEndPoint, Socket>();
        #endregion
        #region 逻辑变量
        IPAddress[] ListenIP;                                                                      // 侦听IP地址集
        int[] ListenPort;                                                                          // 侦听端口集
        ListenMode ServerMode;                                                                     // 侦听模式
        long MessageSize;                                                                          // 接收消息缓存区大小
        bool IsNagle;                                                                              // 是否开启Nagle算法
        long MaxID = 0;                                                                            // 最大SocketID
        #endregion
        #endregion
        #region 构造函数
        /// <summary>
        /// 单端口多IP模式
        /// 输出无连接端口标识
        /// 警告：
        /// 一定要在执行构造函数的外面放置try{}catch{}模块，一旦端口不合格或者被占用，就会抛出错误
        /// </summary>
        /// <param name="IpArray">IP地址数组</param>
        /// <param name="Port">端口1-65535</param>
        /// <param name="ServerMessageSize">缓冲区大小，单位：Byte</param>
        /// <param name="IfNagle">是否开启Nagle算法</param>
        public Server(IPAddress[] IpArray, int Port, long ServerMessageSize, bool IfNagle)
        {
            string ErrorEndPoint = null;                                                           // 创建储存错误网络终结点的字符串
            for (int i = 0; i < IpArray.Length; i = i + 1)                                         // 遍历IP地址
                try { TcpListener tL = new TcpListener(IpArray[i], Port); tL.Start(); tL.Stop(); } // 绑定 监听 停止监听
                catch { ErrorEndPoint = ErrorEndPoint + "\r\n" + IpArray[i] + ":" + Port; }        // 如果监听失败代表已被占用
            if (ErrorEndPoint != null) { throw new ArgumentOutOfRangeException(ErrorEndPoint + "已被占用"); }// 如果不为空就抛出错误
            if (Port < 1 && Port > 65535)                                                          // 检测端口是否符合标准
            {
                throw new ArgumentOutOfRangeException("端口" + Port + "不符合规则");
            }
            else if (ServerMessageSize < 10)
            {
                throw new ArgumentOutOfRangeException("缓冲区过小，小于10");
            }
            else
            {
                ServerMode = ListenMode.Mode1;                                                     // 设定模式
                ListenIP = IpArray;                                                                // 赋值IP
                int[] port = { Port };
                ListenPort = port;                                                                 // 赋值端口
                MessageSize = ServerMessageSize;                                                   // 赋值缓冲区大小
                IsNagle = IfNagle;                                                                 // 赋值是否开启Nagle算法
            }
        }
        #region 重载
        /// <summary>
        /// 多端口多IP模式
        /// 输出标识为：监听IP:监听端口
        /// 警告：
        /// 一定要在执行构造函数的外面放置try{}catch{}模块，一旦端口不合格或者被占用，就会抛出错误
        /// </summary>
        /// <param name="IpArray">IP地址数组</param>
        /// <param name="Port">端口集</param>
        /// <param name="ServerMessageSize">缓冲区大小，单位：Byte</param>
        /// <param name="IfNagle">是否开启Nagle算法</param>
        public Server(IPAddress[] IpArray, int[] Port, long ServerMessageSize, bool IfNagle)
        {
            string ErrorEndPoint = null;
            List<int> ErrorList = new List<int>();
            for (int i = 0; i < Port.Length; i = i + 1)
            {
                if (Port[i] < 1 && Port[i] > 65535)
                {
                    ErrorList.Add(Port[i]);
                    throw new ArgumentOutOfRangeException("端口" + Port[i] + "有误");
                }
                else
                {
                    for (int ia = 0; ia < IpArray.Length; ia = ia + 1)
                        try { TcpListener tL = new TcpListener(IpArray[ia], Port[i]); tL.Start(); tL.Stop(); }
                        catch { ErrorEndPoint = ErrorEndPoint + "\r\n" + IpArray[ia] + ":" + Port[i]; }
                }
            }
            if (ErrorEndPoint != null) { throw new ArgumentOutOfRangeException(ErrorEndPoint + "已被占用"); }
            if (ServerMessageSize < 10)
            {
                throw new ArgumentOutOfRangeException("缓冲区过小，小于10");
            }
            else if (ErrorList.Count < 0)
            {
                ServerMode = ListenMode.Mode2;
                ListenIP = IpArray;
                ListenPort = Port;
                IsNagle = IfNagle;
            }
        }
        #endregion
        #endregion
        #region 端口侦听
        /// <summary>
        /// 开始侦听端口
        /// </summary>
        public void StartListen()
        {
            if (ServerMode == ListenMode.Mode1)
            {
                for (int i = 0; i < ListenIP.Length; i = i + 1)                                    // 循环遍历操作每个IP合上端口的网络终结点
                {
                    IPEndPoint ListenEndPoint = new IPEndPoint(ListenIP[i], ListenPort[0]);        // 根据对应IP和端口创立网络终结点
                    SocketIPEndPointDict.Add(ListenEndPoint, new Dictionary<long, Socket>());      // 在套接字数组内添加对应 网络终结点 键和 套接字字典 值
                    Socket ListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);// 创立监听套接字
                    try
                    {
                        try
                        {
                            ListenSocket.Bind(ListenEndPoint);                                     // 绑定网络终结点
                        }
                        catch (Exception e)
                        {
                            throw new ArgumentOutOfRangeException("在绑定网络终结点时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                        }
                        try
                        {
                            ListenSocket.Listen(1000);                                             // 设为监听模式 设置连接队列上限
                        }
                        catch (Exception e)
                        {
                            throw new ArgumentOutOfRangeException("在设为监听模式时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                        }
                        ListenThreadDict.Add(ListenEndPoint, new Thread(new ThreadStart(() => Listening(ListenEndPoint, ListenSocket))));// 创建监听线程并加入字典
                        ListenThreadDict[ListenEndPoint].IsBackground = true;                      // 设为后台线程
                        ListenThreadDict[ListenEndPoint].Start();                                  // 启动线程
                        ListenSocketDict.Add(ListenEndPoint, ListenSocket);                        // 将监听套接字加入字典
                    }
                    catch (Exception e)
                    {
                        throw new ArgumentOutOfRangeException("在创立套接字时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                    }
                }
            }
            else if (ServerMode == ListenMode.Mode2)
            {
                for (int ia = 0; ia < ListenPort.Length; ia = ia + 1)
                {
                    for (int i = 0; i < ListenIP.Length; i = i + 1)                                // 循环遍历操作每个IP合上端口的网络终结点
                    {
                        IPEndPoint ListenEndPoint = new IPEndPoint(ListenIP[i], ListenPort[ia]);   // 根据对应IP和端口创立网络终结点
                        SocketIPEndPointDict.Add(ListenEndPoint, new Dictionary<long, Socket>());  // 在套接字数组内添加对应 网络终结点 键和 套接字字典 值
                        Socket ListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);// 创立监听套接字
                        try
                        {
                            ListenSocket.Bind(ListenEndPoint);                                     // 绑定网络终结点
                            ListenSocket.Listen(1000);                                             // 设为监听模式 设置连接队列上限
                            ListenThreadDict.Add(ListenEndPoint, new Thread(new ThreadStart(() => Listening(ListenEndPoint, ListenSocket))));// 创建监听线程并加入字典
                            ListenThreadDict[ListenEndPoint].IsBackground = true;                  // 设为后台线程
                            ListenThreadDict[ListenEndPoint].Start();                              // 启动线程
                            ListenSocketDict.Add(ListenEndPoint, ListenSocket);                    // 将监听套接字加入字典
                        }
                        catch (Exception e)
                        {
                            throw new ArgumentOutOfRangeException("在创立套接字时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                        }
                    }
                }
            }

        }
        /// <summary>
        /// 添加端口侦听
        /// 警告：
        /// 一定要在执行构造函数的外面放置try{}catch{}模块，一旦端口不合格或者被占用，就会抛出错误
        /// </summary>
        /// <param name="IP">IP地址</param>
        /// <param name="Port">端口</param>
        public void AddListen(IPAddress IP, int Port)
        {
            if (Port < 1 && Port > 65535)
            {
                throw new ArgumentOutOfRangeException("端口" + Port + "不符合规则");
            }
            else if (ListenIP.Contains(IP) && ListenPort.Contains(Port))
            {
                throw new ArgumentOutOfRangeException(IP + ":" + Port + "已经有了");
            }
            else
            {
                List<IPAddress> Ip = new List<IPAddress>();
                for (int i = 0; i < ListenIP.Length + 1; i = i + 1)
                    if (i != ListenIP.Length + 1)
                        Ip.Add(ListenIP[i]);
                    else
                        Ip.Add(IP);
                ListenIP = Ip.ToArray();
                List<int> port = new List<int>();
                for (int i = 0; i < ListenPort.Length + 1; i = i + 1)
                    if (i != ListenPort.Length + 1)
                        port.Add(ListenPort[i]);
                    else
                        port.Add(Port);
                ListenPort = port.ToArray();
                IPEndPoint ListenEndPoint = new IPEndPoint(IP, Port);                              // 根据对应IP和端口创立网络终结点
                SocketIPEndPointDict.Add(ListenEndPoint, new Dictionary<long, Socket>());           // 在套接字数组内添加对应 网络终结点 键和 套接字字典 值
                Socket ListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);// 创立监听套接字
                try
                {
                    ListenSocket.Bind(ListenEndPoint);                                             // 绑定网络终结点
                    ListenSocket.Listen(1000);                                                     // 设为监听模式 设置连接队列上限
                    ListenThreadDict.Add(ListenEndPoint, new Thread(new ThreadStart(() => Listening(ListenEndPoint, ListenSocket))));// 创建监听线程并加入字典
                    ListenThreadDict[ListenEndPoint].IsBackground = true;                          // 设为后台线程
                    ListenThreadDict[ListenEndPoint].Start();                                      // 启动线程
                    ListenSocketDict.Add(ListenEndPoint, ListenSocket);                            // 将监听套接字加入字典
                }
                catch (Exception e)
                {
                    throw new ArgumentOutOfRangeException("在创立套接字时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                }
            }
        }
        #endregion
        #region 关闭服务端
        /// <summary>
        /// 关闭服务端
        /// </summary>
        public void Stop()
        {
            int SocketNumber = ListenSocketDict.Keys.Count;                                        // 获取监听套接字数量
            for (int i = 0; i < SocketNumber; i = i + 1)                                           // 遍历监听套接字
            {
                ListenSocketDict[ListenSocketDict.Keys.ToArray()[i]].Dispose();                    // 关闭连接释放资源
                ListenSocketDict.Remove(ListenSocketDict.Keys.ToArray()[i]);                       // 从字典中移除
            }
            int ThreadNumber = ListenThreadDict.Keys.Count;                                        // 获取监听线程数量
            for (int i = 0; i < ThreadNumber; i = i + 1)                                           // 遍历监听线程
            {
                if (ListenThreadDict[ListenThreadDict.Keys.ToArray()[i]].ThreadState == ThreadState.Running)// 判断是否还在运行
                {
                    ListenThreadDict[ListenThreadDict.Keys.ToArray()[i]].Join();                   // 关闭线程
                }
            }
            SocketNumber = SocketIPEndPointDict.Keys.Count;                                        // 获取当前Tcp连接套接字字典数量
            for (int i = 0; i < SocketNumber; i = i + 1)                                           // 遍历Tcp套接字字典
            {
                int socketNumber = SocketIPEndPointDict[SocketIPEndPointDict.Keys.ToArray()[i]].Keys.Count;// 获取当前Tcp连接套接字数量
                for (int ia = 0; ia < socketNumber; ia = ia + 1)                                   // 遍历Tcp套接字
                {
                    SocketIPEndPointDict[SocketIPEndPointDict.Keys.ToArray()[i]][SocketIPEndPointDict[SocketIPEndPointDict.Keys.ToArray()[i]].Keys.ToArray()[ia]].Dispose();// 关闭释放套接字
                }
            }
        }
        #endregion
        #region Tcp服务端代码
        /// <summary>
        /// 端口监听方法
        /// </summary>
        /// <param name="ListenIPEndPoint"></param>
        /// <param name="ListenSocket"></param>
        private void Listening(IPEndPoint ListenIPEndPoint, Socket ListenSocket)
        {
            while (ListenSocket.IsBound)                                                           // 确认套接字是否未被停止监听
            {
                try
                {
                    Socket ConnectSocket = ListenSocket.Accept();                                  // 阻塞线程 开始侦听 收到连接 执行下句
                    Thread thread = new Thread(new ThreadStart(() => ConnectDeal(ListenIPEndPoint, ConnectSocket)));
                    thread.IsBackground = true;                                                    // 设为后台线程
                    thread.Start();                                                                // 启动线程
                }
                catch (Exception e)
                {
                    throw new ArgumentOutOfRangeException("在获取客户端连接时出错：\r\n" + e.Message + "\r\n位于：\r\n" + e.Source);
                }
            }
        }
        /// <summary>
        /// 收到连接套接字处理方法
        /// </summary>
        /// <param name="ListenIPEndPoint">监听的网络终结点</param>
        /// <param name="ConnectSocket">连接的套接字</param>
        private void ConnectDeal(IPEndPoint ListenIPEndPoint, Socket ConnectSocket)
        {
            MaxID = MaxID + 1;                                                                     // 更新MaxID
            long MaxId = MaxID;
            ConnectSocket.NoDelay = IsNagle;                                                       // 根据需要开启Nagle算法
            ClientConnent(ListenIPEndPoint, MaxId);                                                // 执行客户端连接委托事件
                                                                                                   //套接字储存字典字典 键对应的字典 添加（   套接字储存字典字典   键对应的字典      的键    的数组             的最后一个键的数值                     + 1 ,Socket套接字）
            SocketIPEndPointDict[ListenIPEndPoint].Add(MaxId, ConnectSocket);                      // 把套接字ID和套接字加入数组
            Thread thread = new Thread(new ThreadStart(() => MessagesReceive(ListenIPEndPoint, ConnectSocket, MaxId)));// 创建消息接收线程
            thread.IsBackground = true;                                                            // 设为后台线程
            thread.Start();                                                                        // 启动线程
        }
        /// <summary>
        /// 消息接收方法
        /// </summary>
        /// <param name="ListenIPEndPoint">对应的网络终结点</param>
        /// <param name="ConnectSocket">连接的套接字</param>
        /// <param name="SocketDictID">套接字ID</param>
        private void MessagesReceive(IPEndPoint ListenIPEndPoint, Socket ConnectSocket, long SocketDictID)
        {
            bool SocketState = true;
            while (SocketState)                                                                    // 确认套接字连接状态
            {
                bool NoError = true;                                                               // 错误标识
                byte[] MessagesArray = new byte[MessageSize];                                      // 创立缓冲区
                int Length = -1;                                                                   // 创立消息长度
                try
                {
                    Length = ConnectSocket.Receive(MessagesArray);                                 // 接收消息写入缓冲区，并获取长度
                }
                catch
                { NoError = false; goto cc; }
                if (NoError == true)
                {
                    ClientMessages(ListenIPEndPoint, SocketDictID, MessagesArray, Length);         // 执行委托的事件，输出消息
                }
                try { SocketState = ConnectSocket.Poll(1, SelectMode.SelectWrite); } catch { goto cc; }// 发生错误直接跳出循环
            }
            // 当结束循环时就证明连接已中断
            // 现在处理连接中断后事
            cc: if (SocketIPEndPointDict[ListenIPEndPoint].ContainsKey(SocketDictID))
            {
                ConnectSocket.Dispose();                                                           // 释放当前套接字
                SocketIPEndPointDict[ListenIPEndPoint].Remove(SocketDictID);                       // 从字典中移除该套接字
                ConnentStop(ListenIPEndPoint, SocketDictID);                                       // 执行客户端终止连接信息委托事件
            }
            GC.Collect();                                                                          // 执行内存清理
        }
        #endregion
        #region 主动执行功能
        /// <summary>
        /// 发送
        /// </summary>
        /// <param name="ListenIPEndPoint"></param>
        /// <param name="SocketID"></param>
        /// <param name="SendContext"></param>
        public void Send(IPEndPoint ListenIPEndPoint, int SocketID, byte[] SendContext)
        {
            SocketIPEndPointDict[ListenIPEndPoint][SocketID].Send(SendContext);
        }
        /// <summary>
        /// 关闭单个连接
        /// </summary>
        /// <param name="ListenIPEndPoint"></param>
        /// <param name="SocketID"></param>
        public void Close(IPEndPoint ListenIPEndPoint, int SocketID)
        {
            SocketIPEndPointDict[ListenIPEndPoint][SocketID].Dispose();
            SocketIPEndPointDict[ListenIPEndPoint].Remove(SocketID);
        }
        #endregion
        /// <summary>
        /// 枚举
        /// </summary>
        private enum ListenMode
        {
            /// <summary>
            /// 单端口多IP模式
            /// </summary>
            Mode1,
            /// <summary>
            /// 多端口多IP模式
            /// </summary>
            Mode2
        }
    }
    /// <summary>
    /// 客户端
    /// </summary>
    public class Client
    {
        #region 变量
        Dictionary<long, Socket> ClientSocketDict = new Dictionary<long, Socket>();
        long MaxID = 0;
        #endregion
        #region 委托
        /// <summary>
        /// 收到信息委托
        /// </summary>
        /// <param name="ConnetID">连接ID</param>
        /// <param name="MessagesArray">消息内容</param>
        /// <param name="Length">消息长度</param>
        public delegate void ConnetMessagesDelegate(long ConnetID, byte[] MessagesArray, long Length);
        /// <summary>
        /// 收到信息委托
        /// </summary>
        public ConnetMessagesDelegate ConnetMessages;
        /// <summary>
        /// 连接断开事件
        /// </summary>
        /// <param name="ConnetID">连接ID</param>
        public delegate void ConnentStopDelegate(long ConnetID);
        /// <summary>
        /// 连接断开事件
        /// </summary>
        public ConnentStopDelegate ConnentStop;
        #endregion
        #region 开始一个连接
        /// <summary>
        /// 开始一个连接
        /// 返回这个连接的连接ID
        /// </summary>
        /// <param name="ServerIP">远程服务器IP</param>
        /// <param name="ServerPort">远程服务器端口</param>
        /// <param name="IsNagle">是否开启Nagle算法</param>
        /// <param name="MessageSize">缓冲区大小，单位：Byte</param>
        public long StartConnet(IPAddress ServerIP, int ServerPort, bool IsNagle, long MessageSize)
        {
            MaxID = MaxID + 1;                                                                     // 更新MaxID
            long MaxId = MaxID;                                                                    // 更新maxID
            Socket ClientConnet = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);// 创建套接字
            ClientConnet.NoDelay = IsNagle;                                                        // 按需要打开Nagle算法
            ClientConnet.Connect(ServerIP, ServerPort);                                            // 连接指定IP地址
            ClientSocketDict.Add(MaxId, ClientConnet);                                             // 添加到字典内
            Task TaskMessagesReceive = new Task(new Action(() => MessagesReceive(MaxId, ClientConnet, MessageSize)));// 执行创建消息接收线程
            TaskMessagesReceive.Start();                                                           // 开始异步操作
            return MaxId;
        }
        #region 重载
        /// <summary>
        /// 开始一个连接
        /// 使用指定的本地地址进行连接
        /// 返回这个连接的连接ID
        /// </summary>
        /// <param name="ServerIP">远程服务器IP</param>
        /// <param name="ServerPort">远程服务器端口</param>
        /// <param name="IsNagle">是否开启Nagle算法</param>
        /// <param name="MessageSize">缓冲区大小，单位：Byte</param>
        /// <param name="LocalIP">本机IP</param>
        /// <param name="LocalPort">本机端口</param>
        public long StartConnet(IPAddress ServerIP, int ServerPort, bool IsNagle, long MessageSize, IPAddress LocalIP, int LocalPort)
        {
            MaxID = MaxID + 1;                                                                     // 更新MaxID
            long MaxId = MaxID;                                                                    // 更新maxID
            Socket ClientConnet = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);// 创建套接字
            ClientConnet.Bind(new IPEndPoint(ServerIP, ServerPort));                               // 绑定本地网络终结点
            ClientConnet.NoDelay = IsNagle;                                                        // 按需要打开Nagle算法
            ClientConnet.Connect(ServerIP, ServerPort);                                            // 连接指定IP地址
            ClientSocketDict.Add(MaxId, ClientConnet);                                             // 添加到字典内
            Task TaskMessagesReceive = new Task(new Action(() => MessagesReceive(MaxId, ClientConnet, MessageSize)));// 执行创建消息接收线程
            TaskMessagesReceive.Start();                                                           // 开始异步操作
            return MaxId;
        }
        #endregion
        #endregion
        #region Client端代码
        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="ClientID">连接ID</param>
        /// <param name="ConnentSocket">连接套接字</param>
        /// <param name="MessageSize"></param>
        public void MessagesReceive(long ClientID, Socket ConnentSocket, long MessageSize)
        {
            bool SocketState = true;
            while (SocketState)                                                                    // 确认套接字连接状态
            {
                bool NoError = true;                                                               // 错误标识
                byte[] MessagesArray = new byte[MessageSize];                                      // 创立缓冲区
                long Length = -1;                                                                  // 创立消息长度
                try
                {
                    Length = ConnentSocket.Receive(MessagesArray);                                 // 接收消息写入缓冲区，并获取长度
                }
                catch
                { NoError = false; goto cc; }
                if (NoError == true)
                {
                    ConnetMessages(ClientID, MessagesArray, Length);                               // 执行委托的事件，输出消息
                }
                try { SocketState = ConnentSocket.Poll(1, SelectMode.SelectWrite); } catch { goto cc; }// 发生错误直接跳出循环
            }
            // 当结束循环时就证明连接已中断
            // 现在处理连接中断后事
            cc: if (ClientSocketDict.ContainsKey(ClientID))
            {
                ConnentSocket.Dispose();                                                           // 释放当前套接字
                ClientSocketDict.Remove(ClientID);                                                 // 从字典中移除该套接字
                ConnentStop(ClientID);                                                             // 执行连接终止连接信息委托事件
            }
            GC.Collect();
        }
        /// <summary>
        /// 终止一个连接
        /// </summary>
        /// <param name="ClientID">连接ID</param>
        /// <param name="Time">等待剩余消息发送的时间</param>
        public void Stop(long ClientID, int Time)
        {
            ClientSocketDict[ClientID].Close(Time);
            ClientSocketDict[ClientID].Dispose();
            ClientSocketDict.Remove(ClientID);
        }
        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="ClientID">连接ID</param> 
        /// <param name="SendContext">发送的内容</param>
        public int Send(long ClientID, byte[] SendContext)
        {
            return ClientSocketDict[ClientID].Send(SendContext);
        }
#region 重载
        /// <summary>
        /// 发送消息
        /// 参考MSDN
        /// </summary>
        /// <param name="ClientID"></param>
        /// <param name="SendContext"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <param name="socketFlags"></param>
        /// <returns></returns>
        public int Send(long ClientID, byte[] SendContext, int offset, int size, SocketFlags socketFlags)
        {
            return ClientSocketDict[ClientID].Send(SendContext, offset, size, socketFlags);
        }
        #endregion
        #endregion
        /// <summary>
        /// 停止所有连接
        /// </summary>
        public void Close()
        {
            for (int i = 0; i < ClientSocketDict.Count; i = i + 1)
            {
                ClientSocketDict[ClientSocketDict.Keys.ToArray()[i]].Close(1000);
                ClientSocketDict[ClientSocketDict.Keys.ToArray()[i]].Dispose();
                ClientSocketDict.Remove(ClientSocketDict.Keys.ToArray()[i]);
            }
        }
    }
}
