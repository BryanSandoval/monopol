using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PlayMate.Fields
{
    /// <summary>
    /// Interaction logic for Field.xaml
    /// </summary>

    public partial class Field : System.Windows.Controls.UserControl
    {
        private string _name;
        private double _price;
        private Image _image;
        private Brush _headerColor;

        #region Accessors

        /// <summary>
        /// Nazwa miasta
        /// </summary>
        public string CityName
        {
            get { return this._name; }
            set { this._name = value; }
        }

        /// <summary>
        /// Cena miasta
        /// </summary>
        public double Price
        {
            get { return this._price; }
            set { this._price = value; }
        }

        /// <summary>
        /// Zdjecie miasta
        /// </summary>
        public Image Image
        {
            get { return this._image; }
            set { this._image = value; }
        }

        /// <summary>
        /// Kolor nag��wka
        /// </summary>
        public Brush HeaderColor
        {
            get { return this._headerColor; }
            set { this._headerColor = value; }
        }

        #endregion

        /// <summary>
        /// Konstruktor pola
        /// </summary>
        /// <param name="_Name">Nazwa miasta</param>
        /// <param name="_Price">Cena miasta</param>
        /// <param name="_Image">Zdj�cie</param>
        /// <param name="_HeaderColor">Kolor nag��wka</param>
        public Field(string _Name,double _Price,Image _Image,Brush _HeaderColor)
        {
            InitializeComponent();
            CityName = _Name;
            Price = _Price;
            Image = _Image;
            HeaderColor = _HeaderColor;
            Load();
        }

        /// <summary>
        /// Pokazanie okna z informacjami o mie�cie
        /// </summary>
        public void Show()
        {
            new ShowField(HeaderColor,CityName,Image,Price.ToString());
        }

        /// <summary>
        /// Prze�adowanie sk�rki
        /// </summary>
        /// <param name="_Name">Nazwa miasta</param>
        /// <param name="_Price">Cena miasta</param>
        /// <param name="_Image">Zdj�cie</param>
        /// <param name="_HeaderColor">Kolor nag��wka</param>
        public void Reload(string _Name, double _Price, Image _Image, Brush _HeaderColor)
        {
            CityName = _Name;
            Price = _Price;
            Image = _Image;
            HeaderColor = _HeaderColor;
            Load();
        }

        /// <summary>
        /// Zastosowanie zmian
        /// </summary>
        public void Load()
        {
            _CityName.Content = CityName;
            _Header.Fill = HeaderColor;
            _Price.Content = Price;
            _Image = Image;
        }
    }
}