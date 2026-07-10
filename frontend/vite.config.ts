import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/** Onde o gateway .NET escuta. Veja `backend/Gateway/appsettings.json`. */
const GATEWAY = 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],

  server: {
    // Proxy de desenvolvimento: o navegador fala com a origem do próprio Vite, e o dev
    // server encaminha /api e /hub para o gateway. Assim o frontend funciona em qualquer
    // porta, e não há CORS envolvido.
    //
    // O gateway *também* configura CORS (ver Program.cs), porque em produção o frontend é
    // servido de outra origem e aí a política é obrigatória. Definir VITE_API_BASE faz o
    // cliente ignorar este proxy e ir direto ao gateway, exercitando o caminho com CORS.
    proxy: {
      '/api': { target: GATEWAY, changeOrigin: true },

      // ws: true é indispensável — o SignalR faz upgrade para WebSocket, e sem isso o
      // proxy responderia 400 ao handshake e o cliente cairia em long-polling.
      '/hub': { target: GATEWAY, changeOrigin: true, ws: true },
    },
  },
});
