fn main() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }

    let mut resource = winresource::WindowsResource::new();
    resource
        .set_icon("../YtecStickyNote/Assets/app-icon.ico")
        .set("CompanyName", "Y-TEC")
        .set("FileDescription", "罫彩 Windows起動待機プログラム")
        .set("FileVersion", "1.5.3.0")
        .set("LegalCopyright", "Copyright © Y-TEC 2026")
        .set("OriginalFilename", "YTEC-Sticky-Note-Startup.exe")
        .set("ProductName", "罫彩")
        .set("ProductVersion", "1.5.3")
        .set_version_info(winresource::VersionInfo::FILEVERSION, 0x0001_0005_0003_0000)
        .set_version_info(
            winresource::VersionInfo::PRODUCTVERSION,
            0x0001_0005_0003_0000,
        )
        .compile()
        .expect("Windows resource metadata could not be compiled");
}
