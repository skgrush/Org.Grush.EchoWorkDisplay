import typing
from typing import NamedTuple

from json import dumps

__incrementalClientMessageId = 1

class RawMessage(NamedTuple):
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

class RawMessageText(RawMessage):
    MessageId: int
    MessageSize: int
    
    Bytes: bytes
    
    ControlCharStart: bytes
    
    def __init__(self, msg: bytes):
        control_char_start = self.get_control_char_start()
        assert len(control_char_start) is 1
        
        global __incrementalClientMessageId
        super().__init__("RawMessageText", fields=[
            ++__incrementalClientMessageId,
            len(msg),
            msg,
            control_char_start
        ])
    
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
    MessageId: int
    MessageSize: int
    Bytes: bytes
    
    ControlCharStart: bytes
    
    @classmethod
    def get_control_char_start(cls):
        return b'\x02'

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
        msg = dumps({
            'Repr': repr(err),
            ## actually traces are probably bad in MicroPython aren't they!
            # 'trace': traceback.format_exception(None, err, err.__traceback__),
        })
        
        return RawMessageError(bytes(msg, 'utf-8'))