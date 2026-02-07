





## Protocol

1. Transmissions are ASCII-Control-Code managed:
2. Transmissions start with ␁ (\u0001) start-of-heading control and ends with ␄ (\u0004) end-of-transmission control.
3. The next 8 bytes are a hex-encoded message identifier (0xFF_FF_FF_FF or 4.2M max) of the message.
4. a null character.
5. The next 8 bytes are an ascii-numeric length of bytes (99,999,999 max) of the subsequent message size (EXCLUDING the start and final control chars).
6. ACK messages start with ␆ (\u0006) ack control and end with ␃ (\u0003) end-text control.
7. ENQ messages start with ␅ (\u0005) enq control and end with ␃ (\u0003) end-text control.
8. JSON Messages start with ␂ (\u0002) start-text control and end with ␃ (\u0003) end-text control.
   1. JSON messages are ALWAYS objects.
9. Non-JSON message transmissions are specified with the ␎ (\u000E) shift-out control:
   1. the next 32 bytes are ascii text and specify the type of transmission.
   2. a null character.
   3. the bytes of the body are then streamed...
   4. finally the ␏ (\u000F) shift-in control follows the body bytes.