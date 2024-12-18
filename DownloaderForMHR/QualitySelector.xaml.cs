using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DownloaderForMHR
{
    /// <summary>
    /// QualitySelector.xaml 的交互逻辑
    /// </summary>
    public partial class QualitySelector : Window
    {
        public QualitySelector(bool isMusicOrVideo, List<string> mediaResolution)
        {
            InitializeComponent();

            lvMediaResolution.ItemsSource = mediaResolution;
            var type = isMusicOrVideo ? "音频音质" : "视频清晰度";
            Title = $"请选择{type}";
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (lvMediaResolution.SelectedIndex == -1)
            {
                MessageBox.Show("请选择一个分辨率。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;// lvMediaResolution.SelectedIndex;

        }

        public int GetSelectionIndex()
        {
            return lvMediaResolution.SelectedIndex;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (lvMediaResolution.SelectedIndex == -1)
            {
                MessageBox.Show("请选择一个分辨率。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
            }
        }
    }
}

