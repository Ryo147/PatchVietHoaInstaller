namespace VietHoaInstaller.Models
{
    public enum GameInstallMode
    {
        /// <summary>Ghi đè trực tiếp file gốc trong thư mục game, có backup để khôi phục (vd: Plague Inc).</summary>
        OverwriteFiles,

        /// <summary>Chỉ copy file mod vào 1 thư mục riêng, không đụng file gốc (vd: RE2R qua Fluffy Mod Manager).</summary>
        CopyToModFolder
    }
}