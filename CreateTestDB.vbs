' Create Visual FoxPro Free Tables (individual DBF files)
Set oConn = CreateObject("ADODB.Connection")
Set fso = CreateObject("Scripting.FileSystemObject")
Set WshShell = CreateObject("WScript.Shell")

' Create directory if it doesn't exist
sDataDir = "C:\HYDRA_AI\"
If Not fso.FolderExists(sDataDir) Then
    fso.CreateFolder(sDataDir)
    WScript.Echo "Created directory: " & sDataDir
End If

' Clean any existing DBF files
WScript.Echo "Cleaning existing DBF files..."
On Error Resume Next
fso.DeleteFile sDataDir & "*.dbf"
fso.DeleteFile sDataDir & "*.fpt"
fso.DeleteFile sDataDir & "*.cdx"
On Error Goto 0

' Use FREE TABLES approach - no DBC container needed
sConnString = "Provider=VFPOLEDB.1;Data Source=" & sDataDir & ";Collating Sequence=MACHINE"

WScript.Echo "Connecting to FoxPro with Free Tables..."
On Error Resume Next
oConn.Open sConnString
If Err.Number <> 0 Then
    WScript.Echo "Error opening connection: " & Err.Description
    WScript.Quit
End If
On Error Goto 0

WScript.Echo "Connected successfully!"

' Create simple tables with basic structure

' 1. Customers Table
WScript.Echo "Creating Customers.dbf..."
sSQL = "CREATE TABLE Customers (" & _
    "ID INTEGER, " & _
    "CustName CHAR(50), " & _
    "City CHAR(50), " & _
    "Phone CHAR(20))"

On Error Resume Next
oConn.Execute sSQL
If Err.Number <> 0 Then
    WScript.Echo "Error creating Customers: " & Err.Description
Else
    WScript.Echo "✓ Customers.dbf created"
End If
On Error Goto 0

' Insert data
oConn.Execute "INSERT INTO Customers VALUES (1, 'Acme Corp', 'New York', '555-1234')"
oConn.Execute "INSERT INTO Customers VALUES (2, 'Tech Solutions', 'London', '555-5678')"

' 2. Products Table
WScript.Echo "Creating Products.dbf..."
sSQL = "CREATE TABLE Products (" & _
    "ID INTEGER, " & _
    "ProdName CHAR(50), " & _
    "Price NUMERIC(10,2), " & _
    "Qty INTEGER)"

On Error Resume Next
oConn.Execute sSQL
If Err.Number <> 0 Then
    WScript.Echo "Error creating Products: " & Err.Description
Else
    WScript.Echo "✓ Products.dbf created"
End If
On Error Goto 0

' Insert data
oConn.Execute "INSERT INTO Products VALUES (101, 'Laptop', 999.99, 50)"
oConn.Execute "INSERT INTO Products VALUES (102, 'Mouse', 29.99, 200)"

' 3. Orders Table
WScript.Echo "Creating Orders.dbf..."
sSQL = "CREATE TABLE Orders (" & _
    "ID INTEGER, " & _
    "CustID INTEGER, " & _
    "OrdDate DATE, " & _
    "Amount NUMERIC(10,2))"

On Error Resume Next
oConn.Execute sSQL
If Err.Number <> 0 Then
    WScript.Echo "Error creating Orders: " & Err.Description
Else
    WScript.Echo "✓ Orders.dbf created"
End If
On Error Goto 0

' Insert data using proper FoxPro date format
oConn.Execute "INSERT INTO Orders VALUES (1001, 1, {^2023-10-01}, 1500.00)"
oConn.Execute "INSERT INTO Orders VALUES (1002, 2, {^2023-10-02}, 299.99)"

' 4. Employees Table
WScript.Echo "Creating Employees.dbf..."
sSQL = "CREATE TABLE Employees (" & _
    "ID INTEGER, " & _
    "FirstName CHAR(50), " & _
    "LastName CHAR(50), " & _
    "Salary NUMERIC(10,2))"

On Error Resume Next
oConn.Execute sSQL
If Err.Number <> 0 Then
    WScript.Echo "Error creating Employees: " & Err.Description
Else
    WScript.Echo "✓ Employees.dbf created"
End If
On Error Goto 0

' Insert data
oConn.Execute "INSERT INTO Employees VALUES (1, 'John', 'Smith', 50000.00)"
oConn.Execute "INSERT INTO Employees VALUES (2, 'Sarah', 'Jones', 60000.00)"

' Close connection
oConn.Close
WScript.Echo "Connection closed."

' Verify files were created
WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "VERIFYING CREATED FILES"
WScript.Echo "========================================"

arrFiles = Array("Customers.dbf", "Products.dbf", "Orders.dbf", "Employees.dbf")
For Each fileName in arrFiles
    filePath = sDataDir & fileName
    If fso.FileExists(filePath) Then
        size = fso.GetFile(filePath).Size
        WScript.Echo "✓ " & fileName & " (" & size & " bytes)"
    Else
        WScript.Echo "✗ " & fileName & " (NOT FOUND)"
    End If
Next

' Show file listing
WScript.Echo ""
WScript.Echo "All files in directory:"
Set folder = fso.GetFolder(sDataDir)
Set files = folder.Files
fileCount = 0
For Each file in files
    If UCase(Right(file.Name, 4)) = ".DBF" Then
        WScript.Echo "  • " & file.Name & " (" & file.Size & " bytes)"
        fileCount = fileCount + 1
    End If
Next

WScript.Echo ""
WScript.Echo "========================================"
WScript.Echo "SUMMARY"
WScript.Echo "========================================"
WScript.Echo "Total DBF files created: " & fileCount
WScript.Echo "Location: " & sDataDir
WScript.Echo ""
WScript.Echo "You can open these DBF files with:"
WScript.Echo "1. Visual FoxPro"
WScript.Echo "2. Microsoft Excel"
WScript.Echo "3. Any DBF viewer/editor"
WScript.Echo ""

' Test query to verify data
WScript.Echo "Testing data retrieval..."
Set oConn2 = CreateObject("ADODB.Connection")
Set oRS = CreateObject("ADODB.Recordset")
oConn2.Open sConnString

oRS.Open "SELECT * FROM Customers", oConn2
WScript.Echo "Customers table has " & oRS.RecordCount & " records"
oRS.Close

oRS.Open "SELECT * FROM Products", oConn2
WScript.Echo "Products table has " & oRS.RecordCount & " records"
oRS.Close

oConn2.Close

MsgBox "Successfully created " & fileCount & " DBF files in:" & vbCrLf & sDataDir