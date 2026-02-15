import typing
from dataclasses import dataclass
from typing import NamedTuple

import framebuf
import uctypes
import json

__incrementalClientMessageId = 1

@dataclass
class RawMessage:
    MessageId: int
    MessageSize: int

    def stream_bytes(self, stream: typing.BinaryIO):
        raise NotImplementedError()
    
    @staticmethod
    def _stream_bytes(message_id: int, start_control: bytes, body: bytes, end_control: bytes, stream: typing.BinaryIO):
        size = len(body)
        
        assert len(start_control) is 1

        message_id_bytes = bytes(f'{message_id:0>8x}', 'utf8')
        assert len(message_id_bytes) is 8

        size_bytes = bytes(f'{size:0>8}', 'utf8')
        assert len(size_bytes) is 8

        stream.write(
            b'\01' +
            message_id_bytes +
            b'\00' +
            size_bytes +
            start_control
        )
        stream.write(body)
        stream.write(
            end_control +
            b'\x04'
        )
        stream.flush()

@dataclass
class RawMessageText(RawMessage):
    Bytes: bytes
    
    ControlCharStart: bytes
    
    @classmethod
    def from_message(cls, msg: bytes):
        control_char_start = cls.get_control_char_start()
        assert len(control_char_start) is 1
        
        global __incrementalClientMessageId
        return cls(
            MessageId=++__incrementalClientMessageId,
            MessageSize=len(msg),
            Bytes=msg,
            ControlCharStart=control_char_start
        )
    
    @classmethod
    def get_control_char_start(cls):
        raise NotImplementedError()
    
    def stream_bytes(self, stream: typing.BinaryIO):
        RawMessage._stream_bytes(
            message_id=self.MessageId,
            start_control=self.ControlCharStart,
            body=self.Bytes,
            end_control=b'\x03',
            stream=stream
        )



class RawMessageJson(RawMessageText):

    def read_body(self) -> typing.Union[BaseJsonBody, None]:
        j = json.loads(self.Bytes)
        
        if type(j) is not dict:
            return None
        msg_type = j.get("$MessageType")
        if msg_type is None:
            return None
        
        del j["$MessageType"]
        j["MessageType"] = msg_type
        
        if msg_type is "NoMediaMessage":
            return RawMessageJson.NoMediaMessage(**j)
        if msg_type is "ButtonPress":
            return RawMessageJson.ButtonPress(**j)
        
        return None
    
    @classmethod
    def get_control_char_start(cls):
        return b'\x02'
    
    @dataclass
    class BaseJsonBody:
        MessageType: str
        
        def to_message(self) -> RawMessageJson:
            return RawMessageJson(bytes(json.dumps(self), 'utf8'))

    @dataclass
    class NoMediaMessage(BaseJsonBody):
        MessageType: typing.Literal["NoMediaMessage"]
    @dataclass
    class ButtonPress(BaseJsonBody):
        MessageType: typing.Literal["ButtonPress"]

        ButtonNumber: int
        State: int

class RawMessageAck(RawMessageText):
    MessageId: int
    MessageSize: int
    
    Bytes: bytes
    
    @classmethod
    def get_control_char_start(cls):
        return b'\x06'
    
    @staticmethod
    def from_message_acknowledgement(acknowledged_message_id: int) -> RawMessageAck:
        return RawMessageAck(
            bytes(f'ackmsg={acknowledged_message_id:0>8x}', 'utf8')
        )

class RawMessageEnq(RawMessageText):
    MessageId: int
    MessageSize: int
    
    Bytes: bytes
    
    @classmethod
    def get_control_char_start(cls):
        return b'\x06'
    
    @staticmethod
    def from_message_ack_request():
        return RawMessageEnq(b'ack-req')

class RawMessageError(RawMessageText):
    MessageId: int
    MessageSize: int
    
    Bytes: bytes
    
    @classmethod
    def get_control_char_start(cls):
        return b'\x07'

    @staticmethod
    def from_error(err: BaseException) -> RawMessageError:
        msg = json.dumps({
            'Repr': repr(err),
            ## actually traces are probably bad in MicroPython aren't they!
            # 'trace': traceback.format_exception(None, err, err.__traceback__),
        })
        
        return RawMessageError(bytes(msg, 'utf-8'))




BITMAP_MSG_FMT = {
    "sk_color_type": (0 | uctypes.ARRAY, 16 | uctypes.UINT8),
    "x_position": (16 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "y_position": (18 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "width": (20 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "height": (22 | uctypes.BIG_ENDIAN | uctypes.UINT16),
    "RESERVED": (24 | uctypes.ARRAY, 8 | uctypes.UINT8),
}

@dataclass
class DrawBitmapMessage(RawMessage):
    SKColor: bytes
    XPosition: int
    YPosition: int
    Width: int
    Height: int
    
    Buffer: bytes
    
    def to_framebuffer(self):
        if self.SKColor is not b'Rgb565':
            raise NotImplementedError(f"sk_color must be Rgb565 but got {self.SKColor}")
        
        if self.Width * self.Height * 2 > len(self.Buffer):
            raise RuntimeError(f"width and height ({self.Width}x{self.Height}) exceed buffered pixel count ({len(self.Buffer) / 2}")

        return framebuf.FrameBuffer(self.Buffer, self.Width, self.Height, framebuf.RGB565)


    @classmethod
    def from_message(cls, type_of_transmission: bytes, buffer: bytes):
        query_params = dict(
            tuple(qp.split("="))
            for qp in
            type_of_transmission.split('?', 1)[1]
            .split('&')
        )

        if query_params['version'] is not '1':
            raise RuntimeError(f"bitmap version must be 1 but got {query_params['version']}")

        if len(type_of_transmission) is not 32:
            raise RuntimeError(f"type_of_transmission must be 32 but got {len(type_of_transmission)}")

        header = uctypes.struct(uctypes.addressof(buffer), BITMAP_MSG_FMT)

        global __incrementalClientMessageId
        return DrawBitmapMessage(
            MessageId=++__incrementalClientMessageId,
            MessageSize=len(buffer) + 32 + 1,
            SKColor=header.sk_color_type.rstrip(b'\x00'),
            XPosition=header.x_position,
            YPosition=header.y_position,
            Width=header.width,
            Height=header.height,
            Buffer=buffer
        )