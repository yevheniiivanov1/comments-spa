// Dev server proxy: the API address comes from Aspire service discovery, or API_URL, or the API's launch profile.
const target = process.env['services__api__http__0'] ?? process.env['API_URL'] ?? 'http://localhost:5180';

export default {
  '/api': { target, secure: false },
  '/files': { target, secure: false },
  '/graphql': { target, secure: false, ws: true },
  '/hubs': { target, secure: false, ws: true },
};
