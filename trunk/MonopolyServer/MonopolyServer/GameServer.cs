using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Reflection;
using System.Diagnostics;

namespace Monopoly
{
    

    class MessageQueue
    {
        public MessageQueue(Socket aSocket)
        {
            mSocket = aSocket;
        }

        Socket mSocket;
        Queue<string> mMessages = new Queue<string>();
        List<byte> mReadBytes = new List<byte>();


        void Read()
        {
            if (mSocket.Available > 0)
            {
                byte[] buf = new byte[mSocket.Available];
                mSocket.Receive(buf);
                mReadBytes.AddRange(buf);
            }


        Again:
            int i = 0;

            while (i < mReadBytes.Count - 1)
            {
                byte b1, b2;
                b1 = mReadBytes[i];
                b2 = mReadBytes[i + 1];
                //if (b1 == 0 && b2 == 0)
                if (b1 == 13 && b2 == 10)
                {
                    byte[] buf = new byte[i];
                    mReadBytes.CopyTo(0, buf, 0, i);
                    mReadBytes.RemoveRange(0, i + 2);


                    mMessages.Enqueue(GameServer.IsoToUnicode(buf));
                    Console.WriteLine("Message from " + mSocket.RemoteEndPoint + ":" +
                        GameServer.IsoToUnicode(buf));

                    goto Again;
                }
                i += 1;
            }
        }

        public string Pop()
        {
            Read();
            return mMessages.Dequeue();
        }

        public int Count
        {
            get
            {
                Read();
                return mMessages.Count;
            }
        }


    }


    class PlayerInfo
    {
        public Player MyPlayer;
        public MessageQueue MyMessageQueue;
        public Socket Connection;
        
    }


    class GameServer
    {
        //List<Player> mPlayers = new List<Player>();
        //List<List<string>> mMessages = new List<List<string>>();
        //List<MessageQueue> mMessageQueues = new List<MessageQueue>();
        public List<PlayerInfo> PlayerInfos = new List<PlayerInfo>();
        public Board GameBoard = new Board();
        uint mMaxPlayers;
        ushort mPort;
        Socket mSocket;
        string mWelcomeMessage;

        public GameServer(ushort aPort, uint aMaxPlayers, string aWelcomeMessage)
        {
            mPort = aPort;
            mMaxPlayers = aMaxPlayers;
            mWelcomeMessage = aWelcomeMessage;
        }











        //NETWORK

        public static byte[] UnicodeToIso(string aStr)
        {
            Encoding iso = Encoding.GetEncoding("iso8859-2");
            byte[] buf = new byte[aStr.Length * 2];
            Buffer.BlockCopy(aStr.ToCharArray(), 0, buf, 0, aStr.Length * 2);
            return Encoding.Convert(Encoding.Unicode, iso, buf, 0, buf.Length);
        }

        public static string IsoToUnicode(byte[] aBuf)
        {
            Encoding iso = Encoding.GetEncoding("iso8859-2");
            byte[] buf = Encoding.Convert(iso, Encoding.Unicode, aBuf);
            char[] charBuf = new char[buf.Length / 2];
            Buffer.BlockCopy(buf, 0, charBuf, 0, buf.Length);
            return new string(charBuf);
        }
  
        public void SendMessage(Socket aSocket, string aMsg)
        {
            byte[] buf = new byte[aMsg.Length + 2];
            byte[] encoded = UnicodeToIso(aMsg);
            Buffer.BlockCopy(encoded, 0, buf, 0, aMsg.Length);
            buf[buf.Length - 2] = 13;
            buf[buf.Length - 1] = 10;
                      

            aSocket.Send(buf);
            Console.WriteLine("Message to " + aSocket.RemoteEndPoint + ":" + aMsg);
        }

        public void SendMessage(PlayerInfo aPlayer, string aMsg)
        {
            SendMessage(aPlayer.Connection, aMsg);
        }

        public void SendMessage(string aMsg)
        {
            foreach(PlayerInfo pi in PlayerInfos)
            {
                if(!pi.MyPlayer.Disconnected)
                    SendMessage(pi, aMsg);
            }
        }

        public void SendMessage(string aType, params object[] aAttributes)
        {
            if (aAttributes.Length % 2 != 0)
                throw new ArgumentException("Number of parameters should be odd");
            string s = "<" + aType + " ";

            for (int i = 0; i < aAttributes.Length - 1; i += 2)
                s += aAttributes[i] + "=\"" + Uri.EscapeDataString(aAttributes[i + 1].ToString()) + "\" ";

     

            s += "/>";
            SendMessage(s);
        }

        //Przetwarza wiadomość chat (niejako w tle). Inny rodzaj wiadomości parsuje i zwraca. Funkcja blokująca!
        public void ProcessGetNextMessage(out PlayerInfo aFrom, out XmlElement aMsg)
        {
            for(;;)
            {
                foreach (PlayerInfo pi in PlayerInfos)
                {
                    if (!pi.MyPlayer.Disconnected)
                    {
                        while (pi.MyMessageQueue.Count != 0)
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.LoadXml(pi.MyMessageQueue.Pop());
                            XmlElement e = (XmlElement)doc.FirstChild;
                            if (e.LocalName == "chat")
                            {
                                SendMessage("<chat from=\"" + pi.MyPlayer.Nickname + "\" message=\"" +
                                    e.GetAttribute("message") + "\"/>");
                            }
                            else
                            {
                                aMsg = (XmlElement)doc.FirstChild;
                                aFrom = pi;
                                return;
                            }
                        }
                    }
                }
                System.Threading.Thread.Sleep(100);
            }
        }







        //GAME
        public void Run()
        {
            AssemblyName an = new AssemblyName(Assembly.GetExecutingAssembly().FullName);
            Console.WriteLine("Starting server ver. " + an.Version + "...");

            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, mPort);
            mSocket = new Socket(AddressFamily.InterNetwork,
                               SocketType.Stream, ProtocolType.Tcp);
            mSocket.Blocking = false;
            mSocket.Bind(localEndPoint);
            mSocket.Listen(10);


            //Czekamy na zgłoszenie gotowości wszystkich graczy
            int numReady = 0;
            while (numReady < PlayerInfos.Count || numReady == 0)
            {
                if (PlayerInfos.Count < mMaxPlayers)
                {
                    Socket client = null;
                    try
                    {
                        client = mSocket.Accept();
                    }
                    catch (Exception)
                    {
                    }

                    if (client != null)
                    {
                        PlayerInfo pi = new PlayerInfo();
                        pi.MyPlayer = new Player();
                        pi.MyMessageQueue = new MessageQueue(client);
                        pi.Connection = client;
                        PlayerInfos.Add(pi);

                        //AssemblyName an = new AssemblyName(Assembly.GetExecutingAssembly().FullName);

                        SendMessage(client, "<welcome serverVersion=\"" + an.Version +
                            "\" message=\"" + mWelcomeMessage + "\">");
                    }
                }

                foreach(PlayerInfo pi in PlayerInfos)
                {
                    MessageQueue q = pi.MyMessageQueue;
                    while (q.Count != 0)
                    {
                        string msg = q.Pop();
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(msg);
                        XmlElement e = (XmlElement)doc.FirstChild;

                        switch(e.LocalName)
                        {
                            case "setNick": 
                                bool taken = false;
                                foreach (PlayerInfo pi2 in PlayerInfos)
                                    if (pi2.MyPlayer.Nickname == e.GetAttribute("nick"))
                                    {
                                        taken = true;
                                        break;
                                    }
                                if (taken)
                                    SendMessage(pi, "<nickTaken/>");
                                else
                                {
                                    SendMessage(pi, "<nickOK/>");
                                    pi.MyPlayer.Nickname = e.GetAttribute("nick");
                                    SendMessage("<newPlayer nick=\"" + e.GetAttribute("nick") + "\"/>");
                                }
                                break;
                            case "ready":
                                if(!pi.MyPlayer.Ready)
                                {
                                    pi.MyPlayer.Ready = true;
                                    numReady++;
                                    SendMessage("<playerReady player=\"" + pi.MyPlayer.Nickname + "\"/>");
                                }

                                break;
                            case "chat":
                                SendMessage("<chat from=\"" + pi.MyPlayer.Nickname + "\" message=\"" 
                                    + e.GetAttribute("message") + "\"/>");
                                break;

                        }

                        
                    }
                }

            }


            //Rozpoczynamy grę
            SendMessage("<allReady/>");
            Console.WriteLine("Starting game!");

            Random rand = new Random();
            int curPlayer = rand.Next(0, PlayerInfos.Count - 1);
            while(true)
            {
                PlayerInfo pi = PlayerInfos[curPlayer];

                if(pi.MyPlayer.TurnsToLeaveJail > 0)
                    pi.MyPlayer.TurnsToLeaveJail--;
                
                if (pi.MyPlayer.TurnsToLeaveJail == 0 && !pi.MyPlayer.Bankrupt && !pi.MyPlayer.Disconnected)
                {
                    int dice1 = rand.Next(1, 6);
                    int dice2 = rand.Next(1, 6);
                    int dstPos = (pi.MyPlayer.Position + dice1 + dice2) % 40;
                    bool passedStart = dstPos < pi.MyPlayer.Position;
                    SendMessage("move", "player", pi.MyPlayer.Nickname, "dice1", dice1, "dice2", dice2,
                        "srcPos", pi.MyPlayer.Position, "dstPos", dstPos, "passedStart", passedStart);

                    pi.MyPlayer.Position = dstPos;
                    if (passedStart)
                        pi.MyPlayer.Money += 200;

                    GameBoard.Fields[dstPos].ServerAction(pi, this, dice1, dice2);

                    //czas na kupno/sprzedaż itp.
                    SendMessage("freeMove", "player", pi.MyPlayer.Nickname,
                        "debtToPay", pi.MyPlayer.Money > 0 ? 0 : -pi.MyPlayer.Money);
                    for (; ; )
                    {
                        bool done = false;
                        PlayerInfo src; XmlElement msg;
                        ProcessGetNextMessage(out src, out msg);
                        if (src == pi)
                        {
                            switch (msg.LocalName)
                            {
                                case "done":
                                    SendMessage("done", "player", pi.MyPlayer.Nickname);
                                    //SendMessage("<done player="
                                    done = true;
                                    break;
                                case "buyHouses":
                                    int num = int.Parse(msg.GetAttribute("number"));
                                    int fieldId = int.Parse(msg.GetAttribute("fieldId"));
                                    if (GameBoard.Fields[fieldId].GetType() == typeof(City) &&
                                        !((City)GameBoard.Fields[fieldId]).Mortgaged &&
                                        ((City)GameBoard.Fields[fieldId]).Group.Monopolist == pi.MyPlayer)
                                    {
                                        City c = (City)GameBoard.Fields[fieldId];
                                        c.NumHouses += num;
                                        pi.MyPlayer.Money -= num * c.PricePerHouse;
                                        SendMessage("housesBought", "player", pi.MyPlayer.Nickname,
                                            "fieldId", fieldId, "number", num, "price", num * c.PricePerHouse);
                                    }
                                    else
                                        Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                            + new StackTrace(true));
                                    break;
                                case "sellHouses":
                                    num = int.Parse(msg.GetAttribute("number"));
                                    fieldId = int.Parse(msg.GetAttribute("fieldId"));
                                    if (GameBoard.Fields[fieldId].GetType() == typeof(City))
                                    {
                                        City c = (City)GameBoard.Fields[fieldId];
                                        if (!c.Mortgaged && c.Group.Monopolist == pi.MyPlayer && c.NumHouses >= num)
                                        {
                                            c.NumHouses -= num;
                                            pi.MyPlayer.Money += num * c.PricePerHouse;
                                            SendMessage("housesSold", "player", pi.MyPlayer.Nickname,
                                                "fieldId", fieldId, "number", num, "price", num * c.PricePerHouse);
                                        }
                                        else
                                            Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") "
                                                + new StackTrace(true));
                                    }
                                    else
                                        Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") "
                                            + new StackTrace(true));
                                    break;
                                case "mortgage":
                                    int propertyId = int.Parse(msg.GetAttribute("propertyId"));
                                    if(GameBoard.Fields[propertyId].GetType().IsSubclassOf(typeof(Property)))
                                    {
                                        Property p = ((Property)GameBoard.Fields[propertyId]);
                                        bool hasHouses = p.GetType() == typeof(City) && ((City)p).NumHouses > 0;
                                        if(p.Owner == src.MyPlayer && !p.Mortgaged && !hasHouses)
                                        {
                                            SendMessage("propertyMortgaged", "propertyId", propertyId, "player", 
                                                src.MyPlayer.Nickname, "mortgage", p.MortgageValue);
                                            p.Mortgaged = true;
                                            src.MyPlayer.Money += p.MortgageValue;
                                        }
                                        else
                                            Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                            + new StackTrace(true));

                                    }
                                    else
                                        Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                            + new StackTrace(true));
                                    break;
                                case "unmortgage":
                                    propertyId = int.Parse(msg.GetAttribute("propertyId"));
                                    if(GameBoard.Fields[propertyId].GetType().IsSubclassOf(typeof(Property)))
                                    {
                                        Property p = ((Property)GameBoard.Fields[propertyId]);
                                        if(p.Owner == src.MyPlayer && p.Mortgaged)
                                        {
                                            int cash = (int)(1.1 * p.MortgageValue);
                                            SendMessage("propertyUnmortgaged", "propertyId", propertyId, "player", 
                                                src.MyPlayer.Nickname, "mortgage", cash);
                                            p.Mortgaged = true;
                                            src.MyPlayer.Money -= cash;
                                        }
                                        else
                                            Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                            + new StackTrace(true));

                                    }
                                    else
                                        Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                            + new StackTrace(true));
                                    break;

                                default:
                                    Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " 
                                        + new StackTrace(true));
                                    break;
                            }


                            if (done)
                            {
                                if (src.MyPlayer.Money < 0)
                                {
                                    SendMessage("bankruptcy", "player", src.MyPlayer.Nickname);
                                    src.MyPlayer.Bankrupt = true;
                                    foreach (Property p in src.MyPlayer.OwnedProperties)
                                    {
                                        p.Owner = null;
                                        p.Mortgaged = false;
                                        if (p.GetType() == typeof(City))
                                            ((City)p).NumHouses = 0;
                                    }

                                    ///TODO: zwrócenie karty wyjścia z więzienia
                                }
                                break;
                            }
                        }
                        else
                            Console.WriteLine("Protocol error! (" + src.MyPlayer.Nickname + ") " +
                                new StackTrace(true));
                    }
                }

                curPlayer = (curPlayer + 1) % PlayerInfos.Count;
            }

            
        }
              

        static void Main(string[] args)
        {
            Console.SetBufferSize(110, 200);
            Console.SetWindowSize(110, 40);
            GameServer gs = new GameServer(8000, 4, "Witamy na serwerze");
            gs.Run();
        }
    }
}
