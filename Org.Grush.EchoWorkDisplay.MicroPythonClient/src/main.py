from machine import Pin

from LcdManager import LcdManager

if __name__ == "__main__":
    
    Pin(24, Pin.OUT).value(1)
    
    manager = LcdManager()
    
    manager.loop()