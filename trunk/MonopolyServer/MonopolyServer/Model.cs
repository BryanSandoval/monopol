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


    abstract class Field
    {
        //public enum FieldType {Go, City, CommunityChest, IncomeTax, Railroad, Chance, Jail, Utility,
        //  FreeParking, GoToJail, LuxuryTax}
        //public FieldType Type;
        public string Name;
        public int Id;

        //public delegate Action(

        public abstract void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2);
    }

    abstract class Property : Field
    {
        public PropertyGroup Group;
        public Player Owner = null;
        public int Price;
        public bool Mortgaged = false;
        public virtual int TotalValue
        {
            get
            {
                return Price;
            }
        }

        public int MortgageValue
        {
            get
            {
                return Price / 2;
            }
        }
        public int UnmortgageValue
        {
            get
            {
                return (int)(1.1 * (Price / 2));
            }
        }
        public abstract int CalculateRent(int aDice1, int aDice2);
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            XmlElement msg;
            if (Owner == null)
            {
                aServer.SendMessage("buyVisitedProperty", "player", aPlayer.Nickname, "fieldId",
                    Id);

                for (; ; )
                {
                    try
                    {
                        if (aServer.ProcessGetNextMessageFrom(aPlayer, true, out msg) &&
                            msg.LocalName == "buyVisitedProperty")
                        {
                            switch (msg.GetAttribute("buy"))
                            {
                                case "true":
                                    Owner = aPlayer;
                                    aPlayer.Money -= Price;
                                    aPlayer.OwnedProperties.Add(this);
                                    aServer.SendMessageToEveryone("<propertyBought player=\"" + aPlayer.Nickname +
                                        "\" fieldId=\"" + aPlayer.Position + "\" price=\"" + Price + "\"/>");

                                    //foreach (Property p in Group.Properties)
                                    //  p.Owner = aPlayer.MyPlayer;

                                    return;
                                case "false":
                                    aServer.SendMessage("auction", "fieldId", Id);
                                    Stopwatch sw = new Stopwatch();
                                    bool firstTimerFired = false;
                                    bool secondTimerFired = false;
                                    sw.Start();
                                    int highestBid = 0;
                                    ServerPlayer highestBidder = null;
                                    while (sw.ElapsedMilliseconds < 15000)
                                    {
                                        ServerPlayer src; XmlElement msg2;
                                        if (aServer.ProcessGetNextMessage(out src, out msg2, false))
                                        {
                                            if (msg2.LocalName != "bid")
                                            {
                                                GameServer.ProtocolError(src);
                                                continue;
                                            }

                                            try
                                            {
                                                int offer = (int)uint.Parse(msg2.GetAttribute("offer"));

                                                if (offer > highestBid)
                                                {
                                                    highestBid = offer;
                                                    highestBidder = src;
                                                    aServer.SendMessage("bid", "player", src.Nickname,
                                                        "offer", offer);
                                                    firstTimerFired = false;
                                                    secondTimerFired = false;
                                                    sw.Reset();
                                                    sw.Start();
                                                }

                                            }
                                            catch (FormatException e)
                                            {
                                                Console.WriteLine(e);
                                            }
                                        }

                                        long time = sw.ElapsedMilliseconds;
                                        if (time > 5000 && !firstTimerFired)
                                        {
                                            aServer.SendMessage("auctionTimer", "timer", 1);
                                            firstTimerFired = true;
                                        }
                                        if (time > 10000 && !secondTimerFired)
                                        {
                                            secondTimerFired = true;
                                            aServer.SendMessage("auctionTimer", "timer", 2);
                                        }


                                    }

                                    if (highestBidder != null)
                                    {
                                        aServer.SendMessage("auctionWinner",
                                            "player", highestBidder.Nickname,
                                            "bid", highestBid);
                                        Owner = highestBidder;
                                        highestBidder.Money -= highestBid;
                                        highestBidder.OwnedProperties.Add(this);

                                        if (highestBidder.Money < 0)
                                        {
                                            aServer.FreeMove(highestBidder, false);
                                        }
                                    }
                                    else
                                        aServer.SendMessage("noAuctionWinner");

                                    return;
                                default:
                                    GameServer.ProtocolError(aPlayer);
                                    break;
                            }
                        }
                        else
                            GameServer.ProtocolError(aPlayer);


                        //Jeżeli są jakieś spóźnione bid'y to trzeba teraz się ich pozbyć, żeby nie było
                        //błędów protokołu
                        System.Threading.Thread.Sleep(500);
                        foreach (ServerPlayer pi in aServer.ServerPlayers)
                            while (pi.MyMessageQueue.Count > 0)
                            {
                                ServerPlayer src;
                                aServer.ProcessGetNextMessage(out src, out msg, false);
                                if (msg != null && msg.LocalName != "bid")
                                    GameServer.ProtocolError(pi);
                            }
                    }
                    catch (SocketException e)
                    {
                        Console.WriteLine(e);
                    }

                }


            }
            else if (Owner != aPlayer && Owner.TurnsToLeaveJail == 0) //ojoj płacimy
            {
                int rent = CalculateRent(aDice1, aDice2);
                aServer.SendMessage("rent", "owner", Owner.Nickname, "player", aPlayer.Nickname,
                    "price", rent);
                aPlayer.Money -= rent;
                Owner.Money += rent;
            }





        }
    }

    class PropertyGroup
    {
        public PropertyGroup(string aName)
        {
            Name = aName;
        }

        public string Name;
        public Player Monopolist
        {
            get
            {
                Player o = null;
                foreach (Property p in Properties)
                {
                    if (o == null)
                        o = p.Owner;
                    else if (o != p.Owner || p.Mortgaged)
                        return null;
                }

                return o;
            }
        }
        /*
                public Player GetOwner()
                {
                    Player o = null;
                    foreach (Property p in Properties)
                    {
                        if (o == null)
                            o = p.Owner;
                        else if (o != p.Owner)
                            return null;
                    }

                    return o;
                }
                */
        public List<Property> Properties = new List<Property>();
    }


    class City : Property
    {
        public int NumHouses = 0; //5 = 1 hotel itd.
        public int[] Rents;
        public int PricePerHouse;
        public override int CalculateRent(int aDice1, int aDice2)
        {
            if (Owner == null || Mortgaged)
                return 0;

            return (NumHouses / Rents.Length) * Rents[Rents.Length - 1] //hotele
                + (NumHouses % Rents.Length) * Rents[NumHouses % Rents.Length]; //domy
        }

        public override int TotalValue
        {
            get
            {
                return Price + NumHouses * PricePerHouse;
            }
        }

        public City(int aId, string aName, PropertyGroup aCountry, int aPrice, int aPricePerHouse, int[] aRents)
        {
            Id = aId;
            Name = aName;
            Price = aPrice;
            Group = aCountry;
            aCountry.Properties.Add(this);
            PricePerHouse = aPricePerHouse;
            Rents = aRents;
        }
    }

    class RailRoad : Property
    {
        public override int CalculateRent(int aDice1, int aDice2)
        {
            if (Owner == null || Mortgaged)
                return 0;

            int ownedRailroads = 0;
            foreach (Property p in Group.Properties)
                if (p.Owner == Owner)
                    ownedRailroads++;

            switch (ownedRailroads)
            {
                case 1: return 25;
                case 2: return 50;
                case 3: return 100;
                case 4: return 200;
            }

            throw new Exception("Internal error");
            //return 0;
        }

        public RailRoad(int aId, string aName, PropertyGroup aGroup)
        {
            Id = aId;
            Name = aName;
            Group = aGroup;
            Price = 200;
        }
    }

    class Utility : Property
    {
        public Utility(int aId, string aName, PropertyGroup aGroup)
        {
            Id = aId;
            Name = aName;
            Price = 150;
            Group = aGroup;
        }

        public override int CalculateRent(int aDice1, int aDice2)
        {
            if (Owner == null || Mortgaged)
                return 0;

            if (Group.Monopolist == Owner)
                return (aDice1 + aDice2) * 10;
            else
                return (aDice1 + aDice2) * 4;
        }


    }

    class DoNothingField : Field
    {
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
        }

        public DoNothingField(int aId)
        {
            Id = aId;
        }
    }


    /*
    class Card
    {
        public abstract void ServerAction(Player aPlayer, int aDice1, int aDice2);
    }

    class GetOutOfJailFreeCard
    {
        public virtual void ServerAction(Player aPlayer, int aDice1, int aDice2)
        {
        }
    }
      */

    class Board
    {
        //Stan planszy, klient powinie go zmieniaŠ tylko poťrednio, wysy│aj╣c polecenia przez sieŠ
        public Field[] Fields;
        public List<Card> CommunityChest;
        public List<Card> CommunityChestUsed = new List<Card>();
        public List<Card> Chances;
        public List<Card> ChancesUsed = new List<Card>();
        public Dictionary<string, PropertyGroup> PropertyGroups = new Dictionary<string, PropertyGroup>();

        public Board()
        {
            /*
       Community Chest: 
          * Advance to Go (Collect $200)
          * Bank error in your favor – collect $200 [£200]
          * Doctor's fee – Pay $50 [£50]
          * Get out of jail free – this card may be kept until needed, or sold
          * Go to jail – go directly to jail – Do not pass Go, do not collect $200 [£200]
          * Grand opera Night – collect $50 from every player for opening night seats [omitted]
          * Tax refund – collect $20 [£20]
          * Life Insurance Matures – collect $100 [omitted]
          * Pay hospital $100 [£100]
          * Pay School tax of $150 [omitted]
          * Receive for services $25 [Receive interest on 7% preference shares: £25]
          * You are assessed for street repairs – $40 per house, $115 per hotel [omitted]
          * You have won second prize in a beauty contest– collect $10 [£10]
          * You inherit $100 [£100]
          * From sale of stock you get $45 [£50]
          * Xmas fund matures - collect $100 [Annuity matures Collect £100]
          * [Pay a £10 fine or take a "Chance"]
          * [Go back to Old Kent Road]
          * [Pay your insurance premium £50]
          * [It is your birthday Collect £10 from each player]
      */

            CommunityChest = new List<Card>(new Card[]
            {
                new AdvanceGoBackToCard(0, 0, true),
                new CollectPayCard(1, 200),
                new CollectPayCard(2, -50),
                new GetOutOfJailFreeCard(3, Card.CardType.CommunityChest),
                new AdvanceGoBackToCard(4, 30, false),
                new CollectPayEachPlayerCard(5, 50),
                new CollectPayCard(6, 20),
                new CollectPayCard(7, 100),
                new CollectPayCard(8, -100),
                new CollectPayCard(9, -150),
                new CollectPayCard(10, 25),
                new RepairsCard(11, 40, 115),
                new CollectPayCard(12, 10),
                new CollectPayCard(13, 100),
                new CollectPayCard(14, 45),
                new CollectPayCard(15, 100)
            });

            RandomizeCards(CommunityChest);

            /* Chances:
            * ADVANCE TO GO (COLLECT $200) [UK card says simply "Advance to 'GO'"]
            * ADVANCE TO ILLINOIS AVE. [ADVANCE TO TRAFALGAR SQUARE]
            * ADVANCE TOKEN TO NEAREST UTILITY. IF UNOWNED you may buy it from bank. IF OWNED, throw dice and pay owner a total ten times the amount thrown. [Does not exist]
            * Advance token to the nearest Railroad and pay owner Twice the Rental to which he is otherwise entitled. If Railroad is unowned, you may buy it from the Bank. [Two such cards in the U.S. version; does not exist in the UK version]
            * ADVANCE TO ST. CHARLES PLACE [Pall Mall] IF YOU PASS GO, COLLECT $200
            * BANK PAYS YOU DIVIDEND OF $50 [£50]
            * GO BACK 3 SPACES
            * GO DIRECTLY TO JAIL DO NOT PASS GO, DO NOT COLLECT $200 [GO TO JAIL MOVE DIRECTLY TO JAIL DO NOT PASS "GO" DO NOT COLLECT £200]
            * Make General Repairs On All Your Property [HOUSES] FOR EACH HOUSE PAY $25 [£25] FOR EACH HOTEL $100 [£100]
            * PAY POOR TAX OF $15 [Does not exist]
            * TAKE A RIDE ON THE READING [Marylebone Station] IF YOU PASS GO COLLECT $200
            * TAKE A WALK ON THE BOARD WALK ADVANCE TOKEN TO BOARD WALK [ADVANCE TO MAYFAIR]
            * THIS CARD MAY BE KEPT UNTIL NEEDED OR SOLD GET OUT OF JAIL FREE [GET OUT OF JAIL FREE This card may be kept until needed or sold]
            * You Have Been ELECTED CHAIRMAN OF THE BOARD PAY EACH PLAYER $50 [Does not exist]
            * YOUR BUILDING AND LOAN MATURES COLLECT $150 [£150]
            * [PAY SCHOOL FEES OF £150]
            * [YOU ARE ASSESSED FOR STREET REPAIRS: £40 PER HOUSE £115 PER HOTEL]
            * ["DRUNK IN CHARGE" FINE £20]
            * [SPEEDING FINE £15]
            * [YOU HAVE WON A CROSSWORD COMPETITION COLLECT £100]
            */

            Chances = new List<Card>(new Card[]
            {
                new AdvanceGoBackToCard(0, 0, true),
                new AdvanceGoBackToCard(1, 24, true),
                new AdvanceToNearestUtilityCard(2),
                new AdvanceToNearestRailroadCard(3),
                new AdvanceGoBackToCard(4, 11, true),
                new CollectPayCard(5, 50),
                new GoBackThreeSpacesCard(6),
                new AdvanceGoBackToCard(7, 30, false),
                new RepairsCard(8, 25, 100),
                new CollectPayCard(9, -15),
                new AdvanceGoBackToCard(10, 5, true),
                new AdvanceGoBackToCard(11, 39, false),
                new GetOutOfJailFreeCard(12, Card.CardType.Chance),
                new CollectPayEachPlayerCard(13, -50),
                new CollectPayCard(14, 150),
                new AdvanceToNearestRailroadCard(15)
            });

            RandomizeCards(Chances);




            PropertyGroups.Add("1", new PropertyGroup("1"));
            PropertyGroups.Add("2", new PropertyGroup("2"));
            PropertyGroups.Add("3", new PropertyGroup("3"));
            PropertyGroups.Add("4", new PropertyGroup("4"));
            PropertyGroups.Add("5", new PropertyGroup("5"));
            PropertyGroups.Add("6", new PropertyGroup("6"));
            PropertyGroups.Add("7", new PropertyGroup("7"));
            PropertyGroups.Add("8", new PropertyGroup("8"));
            PropertyGroups.Add("Railroads", new PropertyGroup("Railroads"));
            PropertyGroups.Add("Utilities", new PropertyGroup("Utilities"));

            Fields = new Field[]
            {
                new DoNothingField(0), 
                new City(1, "1-1", PropertyGroups["1"], 60, 50, new int[]{2, 10, 30, 90, 160, 250}),
                new ChanceCommunityChestField(2, CommunityChest, CommunityChestUsed),
                new City(3, "1-2", PropertyGroups["1"], 60, 50, new int[]{4, 20, 60, 180, 320, 450}),
                new IncomeTaxField(4),
                new RailRoad(5, "Railroads-1", PropertyGroups["Railroads"]),
                new City(6, "2-1", PropertyGroups["2"], 100, 50, new int[]{6, 30, 90, 270, 400, 550}),
                new ChanceCommunityChestField(7, Chances, ChancesUsed),
                new City(8, "2-2", PropertyGroups["2"], 100, 50, new int[]{6, 30, 90, 270, 400, 550}),
                new City(9, "2-3", PropertyGroups["2"], 120, 50, new int[]{8, 40, 100, 300, 450, 600}),
                new DoNothingField(10), 
                new City(11, "3-1", PropertyGroups["3"], 140, 100, new int[]{10, 50, 150, 450, 625, 750}),
                new Utility(12, "Electric Company", PropertyGroups["Utilities"]),
                new City(13, "3-2", PropertyGroups["3"], 140, 100, new int[]{10, 50, 150, 450, 625, 750}),
                new City(14, "3-3", PropertyGroups["3"], 160, 100, new int[]{12, 60, 180, 500, 700, 900}),
                new RailRoad(15, "Railroads-2", PropertyGroups["Railroads"]),
                new City(16, "4-1", PropertyGroups["4"], 180, 100, new int[]{14, 70, 200, 550, 750, 950}),
                new ChanceCommunityChestField(17, CommunityChest, CommunityChestUsed),
                new City(18, "4-2", PropertyGroups["4"], 180, 100, new int[]{14, 70, 200, 550, 750, 950}),
                new City(19, "4-3", PropertyGroups["4"], 200, 100, new int[]{16, 80, 220, 600, 800, 1000}),
                new DoNothingField(20),
                new City(21, "5-1", PropertyGroups["5"], 220, 150, new int[]{18, 90, 250, 700, 875, 1050}),
                new ChanceCommunityChestField(22, Chances, ChancesUsed),
                new City(23, "5-2", PropertyGroups["5"], 220, 150, new int[]{18, 90, 250, 700, 875, 1050}),
                new City(24, "5-3", PropertyGroups["5"], 240, 150, new int[]{20, 100, 300, 750, 925, 1100}),
                new RailRoad(25, "Railroads-3", PropertyGroups["Railroads"]),
                new City(26, "6-1", PropertyGroups["6"], 260, 150, new int[]{22, 110, 330, 800, 975, 1150}),
                new City(27, "6-2", PropertyGroups["6"], 260, 150, new int[]{22, 110, 330, 800, 975, 1150}),
                new Utility(28, "Waterworks", PropertyGroups["Utilities"]),
                new City(29, "6-3", PropertyGroups["6"], 280, 150, new int[]{24, 120, 360, 850, 1025, 1200}),
                new GoToJailField(30),
                new City(31, "7-1", PropertyGroups["7"], 300, 200, new int[]{26, 130, 390, 900, 1100, 1275}),
                new City(32, "7-2", PropertyGroups["7"], 300, 200, new int[]{26, 130, 390, 900, 1100, 1275}),
                new ChanceCommunityChestField(33, CommunityChest, CommunityChestUsed),
                new City(34, "7-3", PropertyGroups["7"], 320, 200, new int[]{28, 150, 450, 1000, 1200, 1400}),
                new RailRoad(35, "Railroads-4", PropertyGroups["Railroads"]),
                new ChanceCommunityChestField(36, Chances, ChancesUsed),
                new City(37, "8-1", PropertyGroups["8"], 350, 200, new int[]{35, 175, 500, 1100, 1300, 1500}),
                new LuxuryTaxField(38),
                new City(39, "8-2", PropertyGroups["8"], 400, 200, new int[]{50, 200, 600, 1400, 1700, 2000})



            };

      
        }

        public static void RandomizeCards(List<Card> aCards)
        {
            Random r = new Random();
            for (int i = 0; i < 100; i++)
            {
                int p1 = r.Next(0, aCards.Count - 1);
                int p2 = r.Next(0, aCards.Count - 1);

                Card c = aCards[p1];
                aCards[p1] = aCards[p2];
                aCards[p2] = c;
            }
        }

    }




    class ChanceCommunityChestField : Field
    {
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            if (Cards.Count == 0)
            {
                Board.RandomizeCards(UsedCards);
                Cards.InsertRange(0, UsedCards);
                UsedCards.Clear();
            }

            Card c = Cards[Cards.Count - 1];
            Cards.Remove(c);

            if (c.ServerAction(aPlayer, aServer, aDice1, aDice2))
                UsedCards.Add(c);
        }

        public List<Card> Cards;
        public List<Card> UsedCards;

        public ChanceCommunityChestField(int aId, List<Card> aCards, List<Card> aUsedCards)
        {
            Id = aId;
            Cards = aCards;
            UsedCards = aUsedCards;


        }
    }

    class IncomeTaxField : Field
    {
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aServer.SendMessage("tenPercentOr200Dollars", "player", aPlayer.Nickname);

            for (; ; )
            {
                XmlElement msg;
                try
                {
                    if (aServer.ProcessGetNextMessageFrom(aPlayer, true, out msg)
                        && msg.LocalName == "tenPercentOr200Dollars")
                    {
                        switch (msg.GetAttribute("type"))
                        {
                            case "tenPercent":
                                int taxBase = aPlayer.Money;
                                foreach (Property p in aPlayer.OwnedProperties)
                                    taxBase += p.TotalValue;

                                aPlayer.Money -= taxBase / 10;

                                aServer.SendMessage("incomeTax", "player", aPlayer.Nickname, "type",
                                    "tenPercent", "tax", taxBase / 10);

                                return;
                            case "200Dollars":
                                aPlayer.Money -= 200;

                                aServer.SendMessage("incomeTax", "player", aPlayer.Nickname, "type",
                                    "200Dollars", "tax", 200);
                                return;
                            default:
                                GameServer.ProtocolError(aPlayer);
                                break;
                        }
                    }
                    else
                        GameServer.ProtocolError(aPlayer);
                }
                catch (XmlException e)
                {
                    Console.WriteLine(e);
                }
            }

        }

        public IncomeTaxField(int aId)
        {
            Id = aId;
        }
    }

    class LuxuryTaxField : Field
    {
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aPlayer.Money -= 75;
            aServer.SendMessage("luxuryTax", "player", aPlayer.Nickname, "tax", 75);
        }

        public LuxuryTaxField(int aId)
        {
            Id = aId;
        }
    }

    class GoToJailField : Field
    {
        public override void ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            if (aPlayer.GetOutOfJailFreeCards.Count != 0)
            {
                aServer.SendMessage("useGetOutOfJailCard", "player", aPlayer.Nickname);
                for (; ; )
                {
                    XmlElement msg;
                    if (aServer.ProcessGetNextMessageFrom(aPlayer, true, out msg)
                        && msg.LocalName == "useGetOutOfJailCard")
                    {
                        switch (msg.GetAttribute("use"))
                        {
                            case "true":
                                GetOutOfJailFreeCard c = aPlayer.GetOutOfJailFreeCards[0];
                                aPlayer.GetOutOfJailFreeCards.Remove(c);
                                if (c.Type == Card.CardType.Chance)
                                    aServer.GameBoard.ChancesUsed.Add(c);
                                else
                                    aServer.GameBoard.CommunityChestUsed.Add(c);

                                aServer.SendMessage("getOutOfJailCardUsed", "player", aPlayer.Nickname);
                                return;
                            //break;
                            case "false":
                                break;
                            default:
                                GameServer.ProtocolError(aPlayer);
                                break;
                        }



                    }
                    else
                        GameServer.ProtocolError(aPlayer);
                }
            }
            
            aPlayer.TurnsToLeaveJail = 3;
            aPlayer.Position = 10;
            aServer.SendMessage("goToJail", "player", aPlayer.Nickname);
        }

        public GoToJailField(int aId)
        {
            Id = aId;
        }
    }

    class Player
    {
        public bool Ready = false;
        public string Nickname;
        public int Position = 0;
        public bool Bankrupt = false;
        public bool Disconnected = false;




        // public Bitmap Token;
        //public Socket Connection;
        // public PlayerController Controller;
        public int Money = 1500;
        public int TurnsToLeaveJail = 0; //0->na wolnoťci
        public List<Property> OwnedProperties = new List<Property>();
        public List<GetOutOfJailFreeCard> GetOutOfJailFreeCards = new List<GetOutOfJailFreeCard>();
    }


    abstract class Card
    {
        public int Id;
        public string Text;
        public enum CardType {Chance, CommunityChest};
        public CardType Type;

        //Zwraca decyzję czy karta ma być przeniesiona do kart zużytych (true) lub czy metoda serveraction
        //sama się nią zajmie
        public abstract bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2);
    }

    class GetOutOfJailFreeCard : Card
    {
        public GetOutOfJailFreeCard(int aId, CardType aType)
        {
            Id = aId;
            Type = aType;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aPlayer.GetOutOfJailFreeCards.Add(this);
            aServer.SendMessage("getOutOfJailCard", "cardId", Id, "player", aPlayer.Nickname);
            return false;
        }
    }

    class AdvanceGoBackToCard : Card
    {
        public AdvanceGoBackToCard(int aId, int aWhere, bool aAdvance)
        {
            Id = aId;
            Where = aWhere;
            Advance = aAdvance;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            bool passedStart = Advance && aPlayer.Position > Where;
            aServer.SendMessage("advanceGoBackToCard", "cardId", Id, "player", aPlayer.Nickname, 
                "srcPos", aPlayer.Position, "dstPos", Where, "advance", Advance, "passedStart", passedStart);
            aPlayer.Position = Where;
            if (passedStart)
                aPlayer.Money += 200;

            return true;
        }

        public int Where;
        public bool Advance;
    }

    class GoBackThreeSpacesCard : Card
    {
        public GoBackThreeSpacesCard(int aId)
        {
            Id = aId;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            int dstPos = aPlayer.Position - 3;
            if (dstPos < 0)
                dstPos += 40;
            aServer.SendMessage("goBackThreeSpacesCard", "cardId", Id, "player", aPlayer.Nickname, 
                "srcPos", aPlayer.Position, "dstPos", dstPos);
            aPlayer.Position = dstPos;
            return true;
        }
    }

    class CollectPayCard : Card
    {
        public CollectPayCard(int aId, int aAmount)
        {
            Id = aId;
            Amount = aAmount;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aServer.SendMessage("collectPayCard", "cardId", Id, "player", aPlayer.Nickname, "amount", Amount);
            aPlayer.Money += Amount;

            return true;
        }

        public int Amount;
    }

    class CollectPayEachPlayerCard : Card
    {
        public CollectPayEachPlayerCard(int aId, int aAmountPerPlayer)
        {
            AmountPerPlayer = aAmountPerPlayer;
            Id = aId;
           
        }
        public int AmountPerPlayer;

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aPlayer.Money -= AmountPerPlayer * aServer.ServerPlayers.Count;
            foreach (ServerPlayer sp in aServer.ServerPlayers)
                sp.Money += AmountPerPlayer;

            aServer.SendMessage("collectPayEachPlayerCard", "cardId", Id, "player", aPlayer.Nickname,
                "amountPerPlayer", AmountPerPlayer);

            return true;
        }
    }

    class RepairsCard : Card
    {
        public RepairsCard(int aId, int aAmountPerHouse, int aAmountPerHotel)
        {
            AmountPerHouse = aAmountPerHouse;
            AmountPerHotel = aAmountPerHotel;
            Id = aId;
        }

        public int AmountPerHouse;
        public int AmountPerHotel;

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            int cost = 0;
            foreach (Property p in aPlayer.OwnedProperties)
                if (p.GetType() == typeof(City))
                {
                    City c = (City)p;
                    cost += (c.NumHouses / 5) * 115;
                    cost += (c.NumHouses % 5) * 40;
                }

            aPlayer.Money -= cost;
            aServer.SendMessage("repairsCard", "cardId", Id, "player", aPlayer.Nickname, "cost", cost);

           

            return true;
        }
    }

    class PayOrDrawCard : Card
    {
        public int Amount;
        public PayOrDrawCard(int aId, int aAmount)
        {
            Amount = aAmount;
            Id = aId;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            aServer.SendMessage("payOrDrawCard", "cardId", Id, "player", aPlayer.Nickname, "amount",
                Amount);


            XmlElement msg;
            if (aServer.ProcessGetNextMessageFrom(aPlayer, true, out msg)
                && msg.LocalName == "payOrDrawCard")
            {
                switch (msg.GetAttribute("decision"))
                {
                    case "pay":
                        aPlayer.Money -= Amount;
                        aServer.SendMessage("payOrDraw", "player", aPlayer.Nickname, "decision",
                            msg.GetAttribute("decision"));
                        
                        break;
                    case "draw":
                        aServer.SendMessage("payOrDraw", "player", aPlayer.Nickname, "decision",
                            msg.GetAttribute("decision"));
                        ((ChanceCommunityChestField)aServer.GameBoard.Fields[aPlayer.Position]).ServerAction
                            (aPlayer, aServer, aDice1, aDice2);

                        break;
                    default:
                        GameServer.ProtocolError(aPlayer);
                        break;
                }
            }
            else
                GameServer.ProtocolError(aPlayer);
            
            return true;
        }
    }

    class AdvanceToNearestUtilityCard : Card
    {
        public AdvanceToNearestUtilityCard(int aId)
        {
            Id = aId;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            for(int i = 0; i < 40; i++)
            {
                int j = (aPlayer.Position + i) % 40;
                Field f = aServer.GameBoard.Fields[j];
                if(f.GetType() == typeof(Utility))
                {
                    Utility u = (Utility)f;
                    aServer.SendMessage("advanceToNearestUtilityCard", "cardId", Id, "player", aPlayer.Nickname,
                            "fieldId", u.Id);
                    if (u.Owner == null)
                        u.ServerAction(aPlayer, aServer, 0, 0);
                    else if (u.Owner != aPlayer)
                    {
                        int r = new Random().Next(1, 6);
                        aPlayer.Money -= 10 * r;
                        u.Owner.Money += 10 * r;
                        aServer.SendMessage("advanceToNearestUtilityPayment", "srcPlayer", aPlayer.Nickname,
                            "dstPlayer", u.Owner.Nickname, "dice", r, "amount", 10 * r);
                    }

                    return true;
                }
                
            }

            throw new Exception("Internal error");
        }
    }

    class AdvanceToNearestRailroadCard : Card
    {
        public AdvanceToNearestRailroadCard(int aId)
        {
            Id = aId;
        }

        public override bool ServerAction(ServerPlayer aPlayer, GameServer aServer, int aDice1, int aDice2)
        {
            for (int i = 0; i < 40; i++)
            {
                int j = (aPlayer.Position + i) % 40;
                Field f = aServer.GameBoard.Fields[j];
                if (f.GetType() == typeof(RailRoad))
                {
                    RailRoad rr = (RailRoad)f;
                    aServer.SendMessage("advanceToNearestRailroadCard", "cardId", Id, "player", aPlayer.Nickname,
                            "fieldId", rr.Id);
                    if (rr.Owner == null)
                        rr.ServerAction(aPlayer, aServer, 0, 0);
                    else if (rr.Owner != aPlayer)
                    {
                        int rent = rr.CalculateRent(aDice1, aDice2) * 2;
                        aPlayer.Money -= rent;
                        rr.Owner.Money += rent;
                        aServer.SendMessage("advanceToNearestRailroadPayment", "srcPlayer", aPlayer.Nickname,
                            "dstPlayer", rr.Owner.Nickname, "amount", rent);
                    }

                    return true;
                }

            }

            throw new Exception("Internal error");
        }


    }



}
