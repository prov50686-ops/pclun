cask "pclun" do
  version "0.5.0"
  sha256 :no_check

  url "https://github.com/prov50686-ops/pclun/releases/download/v#{version}/PcLun-osx-x64.zip"
  name "PcLun"
  desc "Minecraft 1.16.5 + OptiFine launcher"
  homepage "https://github.com/prov50686-ops/pclun"

  app "PcLun.app"
  zap trash: [
    "~/.pclun",
    "~/Library/Preferences/com.mrdomik.pclun.plist",
  ]
end
