sed -i 's/net8.0-windows/net8.0/g' src/WISE.Tests/WISE.Tests.csproj
sed -i '/<UseWPF>true<\/UseWPF>/d' src/WISE.Tests/WISE.Tests.csproj
