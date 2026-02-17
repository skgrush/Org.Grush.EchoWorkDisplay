
try:
    import typing
except ImportError:
    import typings as typing

import sys
import json

from machine import Pin

from raw_messages import RawMessage, RawMessageError, RawMessageAck, RawMessageJson, DrawBitmapMessage
from LCD_2inch import LCD_2inch


class UnexpectedByte(Exception):

    def __init__(self, byte: bytes, msg: str):
        self.byte = byte
        self.msg = msg



class LcdManager(LCD_2inch):

    def __init__(self):
        super().__init__()

        self.pwm.freq(1000)
        self.pwm.duty_u16((self.MAX_PWM_DUTY + 1) // 2)

        self.key0.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler=lambda p: self.key_irq(0, p))
        self.key1.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler=lambda p: self.key_irq(1, p))
        self.key2.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler=lambda p: self.key_irq(2, p))
        self.key3.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler=lambda p: self.key_irq(3, p))

        self.__logo()

        self.show()

    def key_irq(self, number: int, pin: Pin):
        """
        According to the waveshare wiki:
        
        > Each bit has two states of 1 or 0, which means that the key is pressed or released.
        > If the first bit is 1, it means that {KEY} is pressed, and 0 means that {KEY} is released. 
        """

        # since we init with the default, these should trigger on rising and falling, but I'm not sure what the
        # *value* of the pin will be during the irq...

        self._send_message(
            RawMessageJson.ButtonPress(
                MessageType="ButtonPress",
                ButtonNumber=number,
                State=pin.value()
            ).to_message()
        )

    def __logo(self):
        self.fill(self.WHITE)
        color = self.BLUE # should be #143573
        mult = 3
        self.ellipse(mult*int(18.25), mult*36, mult*int(13.5 + 4), mult*int(13.5 + 4), color, True, 0b1100)
        self.ellipse(mult*int(18.25), mult*36, mult*int(13.5 - 4), mult*int(13.5 - 4), self.WHITE, True, 0b1100)
        self.ellipse(mult*int(45.25), mult*36, mult*int(13.5 + 4), mult*int(13.5 + 4), color, True, 0b1100)
        self.ellipse(mult*int(45.25), mult*36, mult*int(13.5 - 4), mult*int(13.5 - 4), self.WHITE, True, 0b1100)

        self.text("Hello, world!", mult*32, 232)

    def show_bitmap_message(self, msg: DrawBitmapMessage):

        fb = msg.to_framebuffer()

        self.blit(fb, msg.XPosition, msg.YPosition)

        self.show()

    def loop(self):
        while True:
            try:
                self.wait_for_message()
            except UnexpectedByte:
                self._read_until_char(b'\x04')
                # read until we find an EOT char
            except Exception as e:
                # uncaught exception, so log it
                if e is not self.__justShownError:
                    self._error(e) # maybe dangerous?

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
        else:
            self._error(UnexpectedByte(message_start_char, f"Expected message start byte {message_start_char}"))

    def _read_ack(self, hex_msg_identifier: bytes, byte_length: int):
        message_body = self._read_bytes_from_stdin(byte_length)

        finalchr = self._read_bytes_from_stdin(1)
        if finalchr is not b'\x0F':
            self._error(UnexpectedByte(finalchr, "Expected final byte"))

        raise NotImplementedError("ACK not implemented")

    def _read_enq(self, hex_msg_identifier: bytes, byte_length: int):
        message_body = self._read_bytes_from_stdin(byte_length)

        finalchr = self._read_bytes_from_stdin(1)
        if finalchr is not b'\x0F':
            self._error(UnexpectedByte(finalchr, "Expected final byte"))

        msg_id = int(hex_msg_identifier, 16)
        if message_body.startswith(b'ack-req'):
            self._send_message(RawMessageAck.from_message_acknowledgement(msg_id))
            return

        raise NotImplementedError("ENQ not implemented")

    def _read_json(self, hex_msg_identifier: bytes, byte_length: int):
        message_body = self._read_bytes_from_stdin(byte_length)

        finalchr = self._read_bytes_from_stdin(1)
        if finalchr is not b'\x0F':
            self._error(UnexpectedByte(finalchr, "Expected final byte"))

        json_msg = json.loads(message_body)

        raise NotImplementedError()
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
            msg = DrawBitmapMessage.from_message(type_of_transmission, message_body)
            return self.show_bitmap_message(msg)

        else:
            return self._error(NotImplementedError(f"Unknown transmission type '{type_of_transmission}'"))

        return
        # TODO

    __justShownError: typing.Union[None, Exception] = None

    def _error(self, err: Exception) -> typing.NoReturn:

        self._justShownError = err

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

        self._send_message(
            RawMessageError.from_error(err)
        )

        self.show()

        raise err

    @staticmethod
    def _send_message(msg: RawMessage):
        msg.stream_bytes(sys.stdout.buffer)

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

            told = sys.stdin.buffer.read()
            told_len = len(told)
            if told_len is 0:
                continue

            target_match = told.find(target_char)

            if target_match is -1:
                continue

            offset = told_len - target_match
            sys.stdin.buffer.seek(-offset, 1)
            return
