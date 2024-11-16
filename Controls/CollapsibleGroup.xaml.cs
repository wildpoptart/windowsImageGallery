using System.Windows;
using System.Windows.Controls;

namespace PhotoOrganizer.Controls
{
    public partial class CollapsibleGroup : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register("Header", typeof(string), typeof(CollapsibleGroup),
                new PropertyMetadata(string.Empty, OnHeaderChanged));

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(object), typeof(CollapsibleGroup),
                new PropertyMetadata(null, OnContentChanged));

        public string Header
        {
            get { return (string)GetValue(HeaderProperty); }
            set { SetValue(HeaderProperty, value); }
        }

        public object Content
        {
            get { return GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }

        private bool isExpanded = true;

        public CollapsibleGroup()
        {
            InitializeComponent();
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CollapsibleGroup)d;
            control.HeaderText.Text = e.NewValue.ToString();
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CollapsibleGroup)d;
            control.ContentArea.Content = e.NewValue;
        }

        private void HeaderButton_Click(object sender, RoutedEventArgs e)
        {
            isExpanded = !isExpanded;
            ContentArea.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExpandCollapseIcon.Text = isExpanded ? "▼" : "►";
        }
    }
} 