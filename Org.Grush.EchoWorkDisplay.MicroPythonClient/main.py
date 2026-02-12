from typing import NoReturn

from json import loads as json_loads

from machine import Pin,SPI,PWM
import framebuf
import sys
import uctypes

BL = 13
DC = 8
RST = 12
MOSI = 11
SCK = 10
CS = 9





class LCD_2inch(framebuf.FrameBuffer):
    """
    
    Source: https://www.waveshare.com/wiki/Pico-LCD-2#Demos
    """

    def __init__(self):
        self.width = 320
        self.height = 240

        self.cs = Pin(CS,Pin.OUT)
        self.rst = Pin(RST,Pin.OUT)

        self.cs(1)
        self.spi = SPI(1)
        self.spi = SPI(1,   1_000_000)
        self.spi = SPI(1, 100_000_000, polarity=0, phase=0, sck=Pin(SCK), mosi=Pin(MOSI), miso=None)
        self.dc = Pin(DC,Pin.OUT)
        self.dc(1)
        self.buffer = bytearray(self.height * self.width * 2)
        super().__init__(self.buffer, self.width, self.height, framebuf.RGB565)
        self.init_display()

        self.RED   =   0x07E0
        self.GREEN =   0x001f
        self.BLUE  =   0xf800
        self.WHITE =   0xffff
        self.BLACK =   0x0000

    def write_cmd(self, cmd):
        self.cs(1)
        self.dc(0)
        self.cs(0)
        self.spi.write(bytearray([cmd]))
        self.cs(1)

    def write_data(self, buf: int):
        self.cs(1)
        self.dc(1)
        self.cs(0)
        self.spi.write(bytearray([buf]))
        self.cs(1)

    def init_display(self):
        """Initialize dispaly"""
        self.rst(1)
        self.rst(0)
        self.rst(1)

        self.write_cmd(0x36)
        self.write_data(0x70)

        self.write_cmd(0x3A)
        self.write_data(0x05)

        self.write_cmd(0xB2)
        self.write_data(0x0C)
        self.write_data(0x0C)
        self.write_data(0x00)
        self.write_data(0x33)
        self.write_data(0x33)

        self.write_cmd(0xB7)
        self.write_data(0x35)

        self.write_cmd(0xBB)
        self.write_data(0x19)

        self.write_cmd(0xC0)
        self.write_data(0x2C)

        self.write_cmd(0xC2)
        self.write_data(0x01)

        self.write_cmd(0xC3)
        self.write_data(0x12)

        self.write_cmd(0xC4)
        self.write_data(0x20)

        self.write_cmd(0xC6)
        self.write_data(0x0F)

        self.write_cmd(0xD0)
        self.write_data(0xA4)
        self.write_data(0xA1)

        self.write_cmd(0xE0)
        self.write_data(0xD0)
        self.write_data(0x04)
        self.write_data(0x0D)
        self.write_data(0x11)
        self.write_data(0x13)
        self.write_data(0x2B)
        self.write_data(0x3F)
        self.write_data(0x54)
        self.write_data(0x4C)
        self.write_data(0x18)
        self.write_data(0x0D)
        self.write_data(0x0B)
        self.write_data(0x1F)
        self.write_data(0x23)

        self.write_cmd(0xE1)
        self.write_data(0xD0)
        self.write_data(0x04)
        self.write_data(0x0C)
        self.write_data(0x11)
        self.write_data(0x13)
        self.write_data(0x2C)
        self.write_data(0x3F)
        self.write_data(0x44)
        self.write_data(0x51)
        self.write_data(0x2F)
        self.write_data(0x1F)
        self.write_data(0x1F)
        self.write_data(0x20)
        self.write_data(0x23)

        self.write_cmd(0x21)

        self.write_cmd(0x11)

        self.write_cmd(0x29)

    def show(self):
        self.write_cmd(0x2A)
        self.write_data(0x00)
        self.write_data(0x00)
        self.write_data(0x01)
        self.write_data(0x3f)

        self.write_cmd(0x2B)
        self.write_data(0x00)
        self.write_data(0x00)
        self.write_data(0x00)
        self.write_data(0xEF)

        self.write_cmd(0x2C)

        self.cs(1)
        self.dc(1)
        self.cs(0)
        self.spi.write(self.buffer)
        self.cs(1)



class LcdManager(LCD_2inch):
    
    MAX_PWM_DUTY = 0xFF_FF
    
    pwm: PWM
    
    
    def __init__(self):
        
        self.pwm = PWM(Pin(BL))
        
        self.pwm.freq(1000)
        self.pwm.duty_u16((self.MAX_PWM_DUTY + 1) // 2)
        
        super().__init__()
        
        self.__logo()
        
        self.show()
    
    
    def __logo(self):
        self.fill(self.WHITE)
        color = self.BLUE # should be #143573
        self.ellipse(18.25, 36, 13.5 + 4, 13.5 + 4, color, False, 0b0110)
        self.ellipse(18.25, 36, 13.5 - 4, 13.5 - 4, self.WHITE, False, 0b0110)
        self.ellipse(45.25, 36, 13.5 + 4, 13.5 + 4, color, False, 0b0110)
        self.ellipse(45.25, 36, 13.5 - 4, 13.5 - 4, self.WHITE, False, 0b0110)
    
        self.text("Hello, world!", 32 - 6 * 8, 51)
    
    def show_bitmap_message(self, type_of_transmission: bytes, buffer: bytes):
        
        query_params = dict(
            tuple(qp.split("="))
                for qp in
                type_of_transmission.split('?', 1)[1]
                .split('&')
        )
        
        if query_params['version'] is not '1':
            self._error(RuntimeError(f"bitmap version must be 1 but got {query_params['version']}"))
        
        if len(type_of_transmission) is not 32:
            self._error(RuntimeError(f"type_of_transmission must be 32 but got {len(type_of_transmission)}"))
        
        header = uctypes.struct(uctypes.addressof(buffer), BITMAP_MSG_FMT)
        
        sk_color: bytes = header.sk_color_type.rstrip(b'\x00')
        x_pos: int = header.x_position
        y_pos: int = header.y_position
        width: int = header.width
        height: int = header.height
        
        if sk_color is not b'Rgb565':
            self._error(NotImplementedError(f"sk_color must be Rgb565 but got {sk_color}"))
        
        if width * height * 2 > len(buffer):
            self._error(RuntimeError(f"width and height ({width}x{height}) exceed buffered pixel count ({len(buffer) / 2}"))
        
        fb = framebuf.FrameBuffer(buffer, width, height, framebuf.RGB565)
        
        self.blit(fb, x_pos, y_pos)
        
        self.show()
        
        self.loop()
    
    def loop(self):
        while True:
            try:
                self.wait_for_message()
            except UnexpectedByte:
                self._read_until_char(b'\x04') # read until we find an EOT char
    
    def wait_for_message(self):
        first_byte = self._read_bytes_from_stdin(1)
        
        if first_byte == b'\x01':
            self.read_transmission(first_byte)
        else:
            self._error(UnexpectedByte(first_byte, "Expected first byte"))
            
        last_byte = self._read_bytes_from_stdin(1)
        if last_byte is not b'\x04':
            self._error(UnexpectedByte(last_byte, "Expected EOT byte"))

    def read_transmission(self, first_byte: bytes):
        
        assert first_byte == '\x01'
    
        hex_msg_identifier = self._read_bytes_from_stdin(8)
        
        nullchr = self._read_bytes_from_stdin(1)
        if nullchr is not b'\x00':
            return self._error(UnexpectedByte(nullchr, "Expected null byte"))
        
        ascii_byte_length = self._read_bytes_from_stdin(8)
        byte_length = int(ascii_byte_length, 10)
        
        message_start_char = self._read_bytes_from_stdin(1)
        
        if message_start_char == b'\x06':
            self._read_ack(hex_msg_identifier, byte_length)
        elif message_start_char == b'\x05':
            self._read_enq(hex_msg_identifier, byte_length)
        elif message_start_char == b'\x02':
            self._read_json(hex_msg_identifier, byte_length)
        elif message_start_char == b'\x0E':
            self._read_shift_out(hex_msg_identifier, byte_length)
        
        
    def _read_ack(self, hex_msg_identifier: bytes, byte_length: int):
        raise NotImplementedError("ACK not implemented")
    
    def _read_enq(self, hex_msg_identifier: bytes, byte_length: int):
        raise NotImplementedError("ENQ not implemented")
    
    def _read_json(self, hex_msg_identifier: bytes, byte_length: int):
        message_body = sys.stdin.buffer.read(byte_length)

        finalchr = self._read_bytes_from_stdin(1)
        if finalchr is not b'\x0F':
            self._error(UnexpectedByte(finalchr, "Expected final byte"))

        json_msg = json_loads(message_body)

        

        # TODO
        
    def _read_shift_out(self, hex_msg_identifier: bytes, byte_length: int):
        type_of_transmission = self._read_bytes_from_stdin(32)
        
        nullchr = self._read_bytes_from_stdin(1)
        
        remaining_byte_length = byte_length - 32 - 1
        
        message_body = self._read_bytes_from_stdin(remaining_byte_length)
        
        finalchr = self._read_bytes_from_stdin(1)
        if finalchr is not b'\x0F':
            self._error(UnexpectedByte(finalchr, "Expected final byte"))
        
        if type_of_transmission.startswith("bitmap?"):
            return self.show_bitmap_message(type_of_transmission, message_body)
        
        return
        # TODO

    def _error(self, err: Exception) -> NoReturn:
        
        self.__logo()
        
        msg = repr(err)
        
        chars_per_row = self.width // 8
        line_y = 60
        
        char_idx = 0
        while char_idx < len(msg) and line_y < self.height:
            line = msg[char_idx:char_idx+chars_per_row]
            
            self.text(line, 0, line_y, self.BLACK)
            
            char_idx += len(line)
            line_y += 8
        
        # TODO send to PC
        
        self.show()
        
        raise err
        
    @staticmethod
    def _read_bytes_from_stdin(n: int) -> bytes:
        return sys.stdin.buffer.read(n)
    
    @staticmethod
    def _read_until_char(target_char: bytes):
        assert len(target_char) is 1

        while True:
            c = sys.stdin.buffer.read(1)
            if c == target_char:
                return
            
            told_len = sys.stdin.buffer.tell()
            if told_len is 0:
                continue
            
            told = sys.stdin.buffer.read(told_len)
            target_match = told.find(target_char)
            
            if target_match is -1:
                continue
                
            offset = told_len - target_match
            sys.stdin.buffer.seek(-offset, 1)
            return



BITMAP_MSG_FMT = {
    "sk_color_type": (0 | uctypes.ARRAY, 16 | uctypes.UINT8),
    "x_position": (16 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "y_position": (18 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "width": (20 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "height": (22 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "RESERVED": (24 | uctypes.ARRAY, 8 | uctypes.UINT8),
}

class UnexpectedByte(Exception):
    
    def __init__(self, byte: bytes, msg: str):
        self.byte = byte
        self.msg = msg