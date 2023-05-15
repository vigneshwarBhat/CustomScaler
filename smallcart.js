import { check, sleep } from 'k6';
import http from "k6/http";

export let options = {
  duration: "5m",
  vus: 10,
  thresholds: {
    
    http_req_duration: ["p(95)<700"]
  }
};

// export let options = {
//   stages: [
//     { duration: '1m', target: 50 },
//     { duration: '1m', target: 150 },
//     { duration: '1m', target: 300 },
//     { duration: '2m', target: 500 },
//     { duration: '2m', target: 800 },
//     { duration: '3m', target: 1200 },
//     { duration: '3m', target: 50 },
//   ],
// };

export default function () {
  let data = { cartId:'3fa85f64-5717-4562-b3fc-2c963f66afa7', cartLine: 10 };
  let cookieHeader='';
  let r = http.post(`http://cart.example.com/api/Cart/item/add`, JSON.stringify(data) ,{
    headers: { 'Content-Type': 'application/json', 'Cookie':'route=1683977792.705.1932.920964|af725159d9fb6db576537fb3c4eb5c8a' }});
    cookieHeader=r.cookies["route"];
  check(r, {
    'status is 200': r => r.status === 200,
  });
  sleep(3);
}

// export default function () {

//   let r = http.get(`http://127.0.0.1:53799/`);
//   check(r, {
//     'status is 200': r => r.status === 200,
//   });
//   sleep(3);
// }