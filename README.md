# Where Is My Head

게임 렌더링 일부를 정책 기반으로 차단해 그래픽카드 자원 사용을 줄이는 PoC Dalamud Plugin.

[Where-Is-My-Head](https://github.com/bloooowfish/Where-Is-My-Head)의 Dalamud Plugin 버전입니다.

## 기능

- Game 2D UI / World-space UI / 3D world ON/OFF 정책 전환
- Dalamud UI를 유지한 상태의 내부 렌더 hook 제어
- VRAM current / budget 표시

## Custom repository URL

```text
https://raw.githubusercontent.com/bloooowfish/Where-Is-My-Head-Plugin/refs/heads/main/repo.json
```

## 참고

Manual DXGI Trim은 driver에 trim을 요청할 뿐이며 즉각적인 VRAM 감소를 보장하지 않습니다.
