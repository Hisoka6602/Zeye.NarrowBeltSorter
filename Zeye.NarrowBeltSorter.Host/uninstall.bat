set serviceName=Zeye.NarrowBeltSorter.Host

sc stop   %serviceName% 
sc delete %serviceName% 

pause