(() => {
  const canvas = document.querySelector("#priceChart");
  if (!canvas) return;

  let candles;
  try {
    candles = JSON.parse(canvas.dataset.candles || "[]").map((bar) => ({
      open: Number(bar.open ?? bar.Open),
      high: Number(bar.high ?? bar.High),
      low: Number(bar.low ?? bar.Low),
      close: Number(bar.close ?? bar.Close),
      volume: Number(bar.volume ?? bar.Volume)
    }));
  } catch {
    return;
  }
  if (!candles.length) return;

  const draw = () => {
    const bounds = canvas.getBoundingClientRect();
    if (!bounds.width || !bounds.height) return;
    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(bounds.width * ratio);
    canvas.height = Math.round(bounds.height * ratio);
    const context = canvas.getContext("2d");
    context.scale(ratio, ratio);

    const width = bounds.width;
    const height = bounds.height;
    const pad = { left: 8, right: 57, top: 13, bottom: 26 };
    const plotWidth = width - pad.left - pad.right;
    const plotHeight = height - pad.top - pad.bottom;
    const minimum = Math.min(...candles.map((bar) => bar.low));
    const maximum = Math.max(...candles.map((bar) => bar.high));
    const range = maximum - minimum || 1;
    const x = (index) => pad.left + (index + .5) * plotWidth / candles.length;
    const y = (price) => pad.top + (maximum - price) / range * plotHeight;

    context.clearRect(0, 0, width, height);
    context.font = "9px Aptos, Segoe UI, sans-serif";
    context.textAlign = "right";
    for (let index = 0; index < 4; index++) {
      const lineY = pad.top + plotHeight * index / 3;
      context.strokeStyle = "#edf0eb";
      context.beginPath();
      context.moveTo(pad.left, lineY);
      context.lineTo(width - pad.right, lineY);
      context.stroke();
      context.fillStyle = "#7d8981";
      context.fillText((maximum - range * index / 3).toFixed(1), width - 4, lineY + 3);
    }

    const volumeMaximum = Math.max(...candles.map((bar) => bar.volume), 1);
    candles.forEach((bar, index) => {
      const center = x(index);
      const bodyWidth = Math.max(2, plotWidth / candles.length * .58);
      const rising = bar.close >= bar.open;
      const color = rising ? "#309562" : "#d26758";
      context.strokeStyle = color;
      context.fillStyle = color;
      context.beginPath();
      context.moveTo(center, y(bar.high));
      context.lineTo(center, y(bar.low));
      context.stroke();
      const bodyTop = y(Math.max(bar.open, bar.close));
      const bodyBottom = y(Math.min(bar.open, bar.close));
      context.fillRect(center - bodyWidth / 2, bodyTop, bodyWidth, Math.max(1, bodyBottom - bodyTop));
      context.globalAlpha = .32;
      context.fillRect(center - bodyWidth / 2, height - 16, bodyWidth, bar.volume / volumeMaximum * 12);
      context.globalAlpha = 1;
    });

    context.fillStyle = "#7d8981";
    context.textAlign = "left";
    context.fillText("09:15", pad.left, height - 4);
    context.textAlign = "center";
    context.fillText("12:00", width / 2, height - 4);
    context.textAlign = "right";
    context.fillText("15:30", width - pad.right, height - 4);
  };

  draw();
  window.addEventListener("resize", draw);
})();