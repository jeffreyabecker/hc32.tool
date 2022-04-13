# hc32.tool
A device flashing tool for the hc32l110 series MCUs

basic usage:
This tool will flash elf / raw binaires to a HC32L110 series MCU via their built-in serial protocol. In order to do this you'll need a usb/ttl virtual com port module. I've successfully used both a CH340 and Raspberry Pi pico.
The chip / serial module should be wired together as such:
```
Serial.DTR/RTS => MCU.P00/RESET
Serial.V+ =>  MCU.V+ 
Serial.GND => MCU.GND
Serial.RXD => MCU.P35
Serial.TXD => MCU.P36
```
once thats done the tool can be invoked as such
`hc32tool download my_app.elf COM7`

about baud rate:
The HC32l110 series UART modules are very limited and coupled to the system clock. My experience is that the default 4mhz clock is only stable up to about 115200 baud which is the default. This produces about a 3s load time for a 16k test rom.