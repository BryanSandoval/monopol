using System;
using System.Collections.Generic;
using System.Text;

namespace MonopolyServer.Model.Card
{
    abstract class Card
    {
        public int Id;
        public string Text;
        public enum CardType { Chance, CommunityChest };
        public CardType Type;

        //Zwraca decyzj� czy karta ma by� przeniesiona do kart zu�ytych (true) lub czy metoda serveraction
        //sama si� ni� zajmie
        public abstract bool ServerAction(GameServer.ServerPlayer aPlayer, GameServer.GameServer aServer, int aDice1, int aDice2);
    }
}
