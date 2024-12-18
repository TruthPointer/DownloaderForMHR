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
using static System.Net.Mime.MediaTypeNames;

namespace DownloaderForMHR
{
    /// <summary>
    /// VideoAudioQualitySelector.xaml 的交互逻辑
    /// </summary>
    public partial class VideoAudioQualitySelector : Window
    {
        public VideoAudioQualitySelector(List<string> videoQualityList, List<string> audioQualityList)
        {
            InitializeComponent();
            LbVideoQuality.ItemsSource = videoQualityList;
            LbAudioQuality.ItemsSource = audioQualityList;
            TbTip.Text = $"注：选项中数值越大，质量越高，{System.Environment.NewLine}所需下载的文件越大。";
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (LbVideoQuality.SelectedIndex == -1)
            {
                MessageBox.Show("请选择视频分辨率。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (LbAudioQuality.SelectedIndex == -1)
            {
                MessageBox.Show("请选择音频音质。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;// lvMediaResolution.SelectedIndex;
        }

        public (int, int) GetSelectionIndex()
        {
            return (LbVideoQuality.SelectedIndex, LbAudioQuality.SelectedIndex);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (LbVideoQuality.SelectedIndex == -1)
            {
                MessageBox.Show("请选择一个分辨率。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
            if (LbVideoQuality.SelectedIndex == -1)
            {
                MessageBox.Show("请选择一个分辨率。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
        }
    }
}
