import json
from dataclasses import dataclass
from urllib.request import urlopen, Request
from collections import defaultdict


@dataclass
class WeatherData:
    city: str
    temp_c: int
    country: str


def get_weather_data(city: str) -> WeatherData:
    req = Request(
        f'https://wttr.in/{city}?format=j1',
        headers={'User-Agent': 'curl'}
    )
    data = json.load(urlopen(req, timeout=20))
    return WeatherData(
        city=city,
        temp_c=int(data['current_condition'][0]['temp_C']),
        country=data['nearest_area'][0]['country'][0]['value'],
    )


if __name__ == '__main__':
    with open('cities.txt', encoding='utf-8') as f:
        cities = list(line.strip() for line in f if line.strip())

    countries = defaultdict(list)
    for city in cities:
        w = get_weather_data(city)
        print(f'{w.city}, {w.country} {w.temp_c:+} °C')

        countries[w.country].append(w.temp_c)

    print()
    for country, temps in countries.items():
        print(f'{country} — {len(temps)} cities, avg: {sum(temps) / len(temps):+.0f} °C, '
            f'min: {min(temps):+} °C, max: {max(temps):+} °C')
