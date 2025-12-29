Get-ChildItem -Path "C:\Users\Patrick Y\Downloads" -Filter "*feedback*" | Select-Object Name, FullName
Get-ChildItem -Path "C:\Users\Patrick Y\Downloads" -Filter "*solution*" | Select-Object Name, FullName
Get-ChildItem -Path "C:\Users\Patrick Y\Downloads" -Filter "*.zip" | Select-Object Name, FullName
