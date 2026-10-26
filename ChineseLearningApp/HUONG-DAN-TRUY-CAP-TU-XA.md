# Mở trang web học từ máy khác và từ xa (NAT)

Từ bản 1.6.0, app **Học tiếng Trung** chạy luôn trang web học bên trong nó. Mở app là trang web chạy, tắt app là trang web tắt. Không cần mở máy chủ riêng.

Vào **Cài đặt → Trang web học** để xem trạng thái và địa chỉ.

## 1. Trên máy này

Bấm **🌐 Mở trang web**, hoặc mở trình duyệt vào `http://localhost:5080`.

## 2. Điện thoại, máy khác cùng Wi-Fi

1. Trong thẻ **Trang web học**, để bật ô **Cho máy khác mở trang web** (mặc định đã bật).
2. Bấm **🛡 Mở cổng tường lửa** rồi bấm **Yes** khi Windows hỏi quyền quản trị. Chỉ cần làm một lần.
   Lần đầu app chạy, Windows cũng có thể tự hỏi "Allow access": chọn **Allow** (nên tích cả mạng Private).
3. Trên điện thoại, mở địa chỉ ở dòng **Trong mạng nhà (Wi-Fi/LAN)**, ví dụ `http://192.168.1.10:5080`.

## 3. Từ xa qua Internet (mở cổng NAT trên router)

1. **Giữ cho máy này luôn một địa chỉ trong mạng nhà:** trong trang quản trị router (thường là `http://192.168.1.1` hoặc `192.168.0.1`, tài khoản in dưới đáy router), vào mục **DHCP / Address Reservation (Static DHCP)** và gán cố định địa chỉ đang hiện ở dòng *Trong mạng nhà* cho máy này.
2. **Chuyển cổng (Port Forwarding / NAT / Virtual Server):** thêm một dòng:
   - Giao thức: **TCP**
   - Cổng ngoài (External/WAN port): `5080` (hoặc cổng khác tùy chọn, ví dụ `8080`)
   - IP trong (Internal IP): địa chỉ của máy này, ví dụ `192.168.1.10`
   - Cổng trong (Internal port): `5080`
3. Trong app bấm **🌍 Xem địa chỉ Internet**, ví dụ `http://113.22.33.44:5080`. Mở địa chỉ đó từ mạng khác, ví dụ 4G trên điện thoại (thử từ cùng Wi-Fi nhiều router sẽ không mở được).
   Nếu ở bước 2 chọn cổng ngoài khác `5080`, thay số cổng tương ứng.

### Nếu vẫn không mở được từ xa

- **Nhà mạng dùng NAT nhiều lớp (CGNAT):** so địa chỉ WAN trong trang router với địa chỉ app hiện. Nếu khác nhau, hoặc WAN có dạng `100.64.x.x`–`100.127.x.x` hay `10.x.x.x`, thì chuyển cổng trên router nhà sẽ không có tác dụng. Khi đó gọi tổng đài nhà mạng (Viettel, FPT, VNPT…) xin **IP public (IP tĩnh/IP động public)**, hoặc dùng dịch vụ đường hầm như Tailscale hay Cloudflare Tunnel.
- **Địa chỉ Internet thay đổi:** phần lớn gói cước gia đình đổi IP sau mỗi lần router khởi động lại. Có thể bật **DDNS** trong router (No-IP, DynDNS…) để có một tên cố định.
- Máy tính phải **đang bật và đang mở app**: tắt app là trang web tắt.

## Lưu ý an toàn

- Trang web chạy bằng `http` (không mã hóa). Mở ra Internet thì mật khẩu và tiến độ học đi qua mạng không mã hóa. Hãy dùng **mật khẩu riêng cho trang học**, không trùng mật khẩu email hay ngân hàng. Không cần dùng từ xa nữa thì xóa dòng chuyển cổng trên router.
- Nhập sai mật khẩu 5 lần liên tiếp thì tài khoản bị khóa 1 phút.
- **Đăng nhập bằng Google chỉ dùng được trên chính máy này** (`http://localhost`). Google không cho đăng nhập trên trang `http://<địa chỉ IP>`. Từ máy khác hãy đăng nhập bằng tên và mật khẩu.
- Bỏ tích **Cho máy khác mở trang web** thì chỉ máy này mở được trang web.
