import json
import socketio
import sys
import argparse
import threading

def RecieveMessage(sio):
	for line in sys.stdin:
		data = line.strip()
		if data == "IS_OPENED":
			print(f"true", flush=True)
			
def main():
	global TOKEN
	parser = argparse.ArgumentParser(description='Запуск скрипта с токеном.')
	parser.add_argument('token', type=str, help='Токен для подключения')
	args = parser.parse_args()
	TOKEN = args.token
			
	sio = socketio.Client(reconnection=True, reconnection_attempts=5, reconnection_delay=5)

	# Запуск функции RecieveMessage в отдельном потоке
	recv_thread = threading.Thread(target=RecieveMessage, daemon=True, args=[sio,])
	recv_thread.start()

	@sio.on('connect')
	def on_connect():
		sio.emit('add-user', {"token": TOKEN, "type": "alert_widget"})

	@sio.on('donation')
	def on_message(data):
		y = json.loads(data)
		print(f"alert_type: `{y['alert_type']}`, date: `{y['date_created']}`, username: `{y['username']}`, amount: `{y['amount']}`, currency: `{y['currency']}`", flush=True)
		
	@sio.event
	def connect_error(data):
		print(f"Ошибка подключения: {data}. Возможно, вы используете VPN.")

	try: sio.connect('wss://socket.donationalerts.ru:443', transports='websocket')
	except: pass

	sio.wait()

if __name__ == "__main__":
    main()
